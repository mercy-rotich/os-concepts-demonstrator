using System;
using System.Linq;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using OS_Concepts_Demonstrator.Models;

namespace OS_Concepts_Demonstrator.Controls;

public class MemoryUsageChart : Control
{
    // The chart receives full readings (timestamp + percentage), not bare numbers,
    // so it can label the X-axis with the real time each reading was recorded.
    public static readonly StyledProperty<IEnumerable<MemoryHistoryPoint>> PointsProperty =
        AvaloniaProperty.Register<MemoryUsageChart, IEnumerable<MemoryHistoryPoint>>(
            nameof(Points),
            Array.Empty<MemoryHistoryPoint>()
        );

    public IEnumerable<MemoryHistoryPoint> Points
    {
        get => GetValue(PointsProperty);
        set => SetValue(PointsProperty, value);
    }

    static MemoryUsageChart()
    {
        // Registered once for the whole class (not once per instance),
        // so the chart redraws whenever Points is replaced.
        AffectsRender<MemoryUsageChart>(PointsProperty);

        // Redraw whenever the control is resized, and never paint outside it.
        AffectsRender<MemoryUsageChart>(BoundsProperty);
        ClipToBoundsProperty.OverrideDefaultValue<MemoryUsageChart>(true);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        double width = Bounds.Width;
        double height = Bounds.Height;

        context.DrawRectangle(
            new SolidColorBrush(Color.Parse("#171722")),
            null,
            new Rect(0, 0, width, height)
        );

        double leftMargin = 45;
        double rightMargin = 20;
        double topMargin = 20;
        double bottomMargin = 35;

        double graphWidth = width - leftMargin - rightMargin;
        double graphHeight = height - topMargin - bottomMargin;

        if (graphWidth <= 0 || graphHeight <= 0)
            return;

        double[] percentages = { 0, 25, 50, 75, 100 };

        foreach (double percentage in percentages)
        {
            double y = topMargin +
                       graphHeight * (1 - percentage / 100);

            context.DrawLine(
                new Pen(Brushes.DimGray, 1),
                new Point(leftMargin, y),
                new Point(leftMargin + graphWidth, y)
            );

            var formattedText = new FormattedText(
                $"{percentage}%",
                System.Globalization.CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                new Typeface("Arial"),
                11,
                Brushes.White
            );

            context.DrawText(formattedText, new Point(5, y - 8));
        }

        // Axes
        context.DrawLine(
            new Pen(Brushes.White, 1),
            new Point(leftMargin, topMargin),
            new Point(leftMargin, topMargin + graphHeight)
        );

        context.DrawLine(
            new Pen(Brushes.White, 1),
            new Point(leftMargin, topMargin + graphHeight),
            new Point(leftMargin + graphWidth, topMargin + graphHeight)
        );

        // Memory usage line
        var points = (Points ?? Array.Empty<MemoryHistoryPoint>()).ToList();
        var values = points.Select(p => p.UsagePercentage).ToList();

        if (values.Count == 0)
        {
            DrawCenteredMessage(context, "Collecting data - first reading in a few seconds...",
                leftMargin, topMargin, graphWidth, graphHeight);
            return;
        }

        if (values.Count >= 1)
        {
            var linePen = new Pen(
                new SolidColorBrush(Color.Parse("#38BDF8")),
                2
            );

            for (int i = 0; i < values.Count - 1; i++)
            {
                double x1 = XFor(i, values.Count, leftMargin, graphWidth);
                double x2 = XFor(i + 1, values.Count, leftMargin, graphWidth);

                double y1 = topMargin +
                    graphHeight * (1 - values[i] / 100);

                double y2 = topMargin +
                    graphHeight * (1 - values[i + 1] / 100);

                context.DrawLine(
                    linePen,
                    new Point(x1, y1),
                    new Point(x2, y2)
                );
            }

            // Markers are drawn AFTER the line so they sit on top of it.
            var markerFill = new SolidColorBrush(Color.Parse("#38BDF8"));
            var markerOutline = new Pen(new SolidColorBrush(Color.Parse("#171722")), 2);

            for (int i = 0; i < values.Count; i++)
            {
                double x = XFor(i, values.Count, leftMargin, graphWidth);

                // Clamp so a bad value can never draw outside the plot area.
                double clamped = Math.Clamp(values[i], 0, 100);
                double y = topMargin + graphHeight * (1 - clamped / 100);

                context.DrawEllipse(markerFill, markerOutline, new Point(x, y), 4, 4);
            }
        }

        DrawTimeLabels(context, points, leftMargin, topMargin + graphHeight, graphWidth);
    }

    /// <summary>
    /// X position of reading number <paramref name="index"/>. One reading is centred;
    /// two or more are spread evenly from the left edge to the right edge.
    /// </summary>
    private static double XFor(int index, int count, double left, double graphWidth)
    {
        if (count <= 1)
            return left + graphWidth / 2;

        return left + (double)index / (count - 1) * graphWidth;
    }

    private static void DrawCenteredMessage(
        DrawingContext context, string message,
        double left, double top, double width, double height)
    {
        var text = new FormattedText(
            message,
            System.Globalization.CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            new Typeface("Arial"),
            13,
            Brushes.Gray)
        {
            MaxTextWidth = Math.Max(1, width),
            TextAlignment = TextAlignment.Center
        };

        context.DrawText(text, new Point(left, top + (height - text.Height) / 2));
    }

    /// <summary>
    /// Draws HH:mm:ss labels under the X-axis. Only as many labels as fit
    /// are drawn (spaced evenly, always including first and last), so they never overlap.
    /// </summary>
    private static void DrawTimeLabels(
        DrawingContext context,
        List<MemoryHistoryPoint> points,
        double left,
        double axisY,
        double graphWidth)
    {
        if (points.Count == 0)
            return;

        const double minLabelSpacing = 80; // pixels needed per label
        int maxLabels = Math.Max(2, (int)(graphWidth / minLabelSpacing) + 1);
        int labelCount = points.Count == 1 ? 1 : Math.Min(points.Count, maxLabels);

        var tickPen = new Pen(Brushes.White, 1);

        for (int n = 0; n < labelCount; n++)
        {
            // Spread chosen indices evenly from first (0) to last (Count-1).
            int index = labelCount == 1
                ? 0
                : (int)Math.Round((double)n * (points.Count - 1) / (labelCount - 1));
            double x = XFor(index, points.Count, left, graphWidth);

            context.DrawLine(tickPen, new Point(x, axisY), new Point(x, axisY + 4));

            var text = new FormattedText(
                points[index].RecordedAt.ToString("HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture),
                System.Globalization.CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                new Typeface("Arial"),
                11,
                Brushes.White
            );

            // Centre the label on its tick, but keep it inside the control.
            double labelX = Math.Clamp(x - text.Width / 2, 0, Math.Max(0, left + graphWidth + 20 - text.Width));
            context.DrawText(text, new Point(labelX, axisY + 8));
        }
    }
}
