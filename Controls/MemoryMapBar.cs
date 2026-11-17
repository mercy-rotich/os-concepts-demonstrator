using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using OS_Concepts_Demonstrator.Models;

namespace OS_Concepts_Demonstrator.Controls;

// Draws SIMULATED memory as one horizontal bar from address 0 (left) to the end (right).
// Each block's width is proportional to its size. Allocated blocks are solid colours,
// free holes are dark grey with diagonal hatching, and the selected block gets a white outline.
public class MemoryMapBar : Control
{
    public static readonly StyledProperty<IEnumerable<MemoryBlockModel>> BlocksProperty =
        AvaloniaProperty.Register<MemoryMapBar, IEnumerable<MemoryBlockModel>>(
            nameof(Blocks), Array.Empty<MemoryBlockModel>());

    public static readonly StyledProperty<int> TotalKbProperty =
        AvaloniaProperty.Register<MemoryMapBar, int>(nameof(TotalKb), 1);

    public static readonly StyledProperty<int> SelectedAllocationIdProperty =
        AvaloniaProperty.Register<MemoryMapBar, int>(nameof(SelectedAllocationId));

    public IEnumerable<MemoryBlockModel> Blocks
    {
        get => GetValue(BlocksProperty);
        set => SetValue(BlocksProperty, value);
    }

    public int TotalKb
    {
        get => GetValue(TotalKbProperty);
        set => SetValue(TotalKbProperty, value);
    }

    public int SelectedAllocationId
    {
        get => GetValue(SelectedAllocationIdProperty);
        set => SetValue(SelectedAllocationIdProperty, value);
    }

    private static readonly Typeface LabelFont = new("Arial");

    static MemoryMapBar()
    {
        AffectsRender<MemoryMapBar>(BlocksProperty, TotalKbProperty, SelectedAllocationIdProperty, BoundsProperty);
        ClipToBoundsProperty.OverrideDefaultValue<MemoryMapBar>(true);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        const double axisHeight = 20;
        double width = Bounds.Width;
        double barHeight = Bounds.Height - axisHeight;

        if (width <= 0 || barHeight <= 0 || TotalKb <= 0)
            return;

        context.DrawRectangle(new SolidColorBrush(Color.Parse("#171722")), null, new Rect(0, 0, width, barHeight));

        double kbToPx = width / TotalKb;
        var hatchPen = new Pen(new SolidColorBrush(Color.Parse("#4B5563")), 1);
        var separatorPen = new Pen(new SolidColorBrush(Color.Parse("#0F0F17")), 2);

        foreach (MemoryBlockModel block in Blocks)
        {
            var rect = new Rect(block.StartKb * kbToPx, 0, block.SizeKb * kbToPx, barHeight);

            context.DrawRectangle(new SolidColorBrush(Color.Parse(block.StatusColor)), null, rect);

            if (block.IsFree)
                DrawHatching(context, rect, hatchPen);

            // Thin dark line between blocks so adjacent blocks never blur together.
            context.DrawLine(separatorPen, rect.TopLeft, rect.BottomLeft);

            DrawBlockLabel(context, block, rect);
        }

        // Selected block outline drawn last so it sits on top.
        foreach (MemoryBlockModel block in Blocks)
        {
            if (!block.IsFree && block.AllocationId == SelectedAllocationId && SelectedAllocationId != 0)
            {
                var rect = new Rect(block.StartKb * kbToPx, 0, block.SizeKb * kbToPx, barHeight).Deflate(1.5);
                context.DrawRectangle(null, new Pen(Brushes.White, 3), rect);
            }
        }

        // Address axis: start and end of simulated memory.
        DrawText(context, "0 KB", new Point(0, barHeight + 3), 11, Brushes.Gray);
        var endText = MakeText($"{TotalKb} KB", 11, Brushes.Gray);
        context.DrawText(endText, new Point(width - endText.Width, barHeight + 3));
    }

    private static void DrawHatching(DrawingContext context, Rect rect, Pen pen)
    {
        using (context.PushClip(rect))
        {
            const double spacing = 10;
            for (double x = rect.Left - rect.Height; x < rect.Right; x += spacing)
            {
                context.DrawLine(pen, new Point(x, rect.Bottom), new Point(x + rect.Height, rect.Top));
            }
        }
    }

    // Writes the owner and size inside the block, but only when it fits; tiny blocks stay unlabelled
    // (their details are in the block list below the bar).
    private static void DrawBlockLabel(DrawingContext context, MemoryBlockModel block, Rect rect)
    {
        IBrush brush = block.IsFree ? Brushes.LightGray : Brushes.Black;
        string title = block.IsFree ? "free" : block.ProcessName;

        var titleText = MakeText(title, 12, brush, FontWeight.Bold);
        var sizeText = MakeText($"{block.SizeKb} KB", 11, brush);

        double needed = Math.Max(titleText.Width, sizeText.Width) + 8;
        if (rect.Width < needed)
            return;

        double centreX = rect.Left + rect.Width / 2;
        double top = rect.Top + (rect.Height - titleText.Height - sizeText.Height) / 2;

        using (context.PushClip(rect))
        {
            context.DrawText(titleText, new Point(centreX - titleText.Width / 2, top));
            context.DrawText(sizeText, new Point(centreX - sizeText.Width / 2, top + titleText.Height));
        }
    }

    private static void DrawText(DrawingContext context, string text, Point at, double size, IBrush brush)
        => context.DrawText(MakeText(text, size, brush), at);

    private static FormattedText MakeText(string text, double size, IBrush brush, FontWeight weight = FontWeight.Normal)
        => new(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
               new Typeface(LabelFont.FontFamily, FontStyle.Normal, weight), size, brush);
}
