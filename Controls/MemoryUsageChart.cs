using System;
using System.Linq;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace OS_Concepts_Demonstrator.Controls;

public class MemoryUsageChart : Control
{
    public static readonly StyledProperty<IEnumerable<double>> ValuesProperty =
        AvaloniaProperty.Register<MemoryUsageChart, IEnumerable<double>>(
            nameof(Values),
            new List<double>()
        );

    public IEnumerable<double> Values
    {
        get => GetValue(ValuesProperty);
        set => SetValue(ValuesProperty, value);
    }

    public MemoryUsageChart()
{
    ValuesProperty.Changed.AddClassHandler<MemoryUsageChart>(
        (chart, args) => chart.InvalidateVisual()
    );
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
        var values = Values.ToList();

        if (values.Count >= 2)
        {
            var linePen = new Pen(
                new SolidColorBrush(Color.Parse("#38BDF8")),
                2
            );

            for (int i = 0; i < values.Count - 1; i++)
            {
                double x1 = leftMargin +
                    (double)i / (values.Count - 1) * graphWidth;

                double x2 = leftMargin +
                    (double)(i + 1) / (values.Count - 1) * graphWidth;

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
        }
    }
}
