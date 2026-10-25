using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Vatowiec.SharedContracts;

namespace Vatowiec.Desktop;

public sealed class ColumnLayoutEventArgs(string columnId, double? width = null, string? beforeColumnId = null) : EventArgs
{
    public string ColumnId { get; } = columnId;
    public double? Width { get; } = width;
    public string? BeforeColumnId { get; } = beforeColumnId;
}

// Captures the stable presenter, so changing cell widths does not lose the pointer.
internal sealed class TableHeaderGestures
{
    private readonly TableCells owner;
    private readonly Action<ColumnLayoutEventArgs> preview;
    private readonly Action<ColumnLayoutEventArgs> commit;
    private readonly Action<double?> insertion;
    private string? column;
    private double startX, originalWidth, currentWidth;
    private bool resizing, dragging;
    private string? before;
    public TableHeaderGestures(TableCells owner, Action<ColumnLayoutEventArgs> preview, Action<ColumnLayoutEventArgs> commit, Action<double?> insertion)
    {
        this.owner = owner; this.preview = preview; this.commit = commit; this.insertion = insertion;
        owner.PointerPressed += Pressed; owner.PointerMoved += Moved; owner.PointerReleased += Released;
        owner.PointerCaptureLost += (_, _) => Cancel();
    }
    private double WindowX(PointerEventArgs e) => e.GetPosition(TopLevel.GetTopLevel(owner)).X;
    private void Pressed(object? sender, PointerPressedEventArgs e)
    {
        if (owner.Value != null || !e.GetCurrentPoint(owner).Properties.IsLeftButtonPressed) return;
        var x = e.GetPosition(owner).X; var edge = 0d;
        foreach (var c in owner.Columns ?? [])
        {
            edge += c.Width ?? TableLayouts.DefaultWidth(c.Id);
            if (Math.Abs(x - edge) <= 6) { Start(c, true, e); return; }
        }
        edge = 0;
        foreach (var c in owner.Columns ?? [])
        {
            var width = c.Width ?? TableLayouts.DefaultWidth(c.Id); edge += width;
            if (x >= edge - width && x <= edge)
            {
                Start(c, false, e); return;
            }
        }
    }
    private void Start(TableColumn c, bool resize, PointerPressedEventArgs e)
    {
        column = c.Id; originalWidth = currentWidth = c.Width ?? TableLayouts.DefaultWidth(c.Id); startX = WindowX(e);
        resizing = resize; dragging = false; before = null;
        e.Pointer.Capture(owner); e.Handled = true;
    }
    private void Moved(object? sender, PointerEventArgs e)
    {
        if (owner.Value != null) return;
        var x = e.GetPosition(owner).X;
        if (column == null)
        {
            var edge = 0d; var resize = false;
            foreach (var c in owner.Columns ?? []) { edge += c.Width ?? TableLayouts.DefaultWidth(c.Id); if (Math.Abs(x - edge) <= 6) resize = true; }
            owner.Cursor = new Cursor(resize ? StandardCursorType.SizeWestEast : StandardCursorType.Arrow); return;
        }
        if (resizing)
        {
            currentWidth = Math.Clamp(originalWidth + WindowX(e) - startX, TableLayouts.MinimumWidth, TableLayouts.MaximumWidth);
            owner.Cursor = new Cursor(StandardCursorType.SizeWestEast);
            preview(new(column, currentWidth)); e.Handled = true; return;
        }
        if (!dragging && Math.Abs(WindowX(e) - startX) < 5) return;
        dragging = true; owner.Cursor = new Cursor(StandardCursorType.Hand);
        var position = 0d; before = null;
        foreach (var c in owner.Columns ?? [])
        {
            var width = c.Width ?? TableLayouts.DefaultWidth(c.Id);
            if (c.Id != column && x < position + width / 2) { before = c.Id; break; }
            position += width;
        }
        insertion(position); e.Handled = true;
    }
    private void Released(object? sender, PointerReleasedEventArgs e)
    {
        if (column == null) return;
        var id = column; var resize = resizing; var moved = dragging; var target = before;
        column = null; resizing = dragging = false; insertion(null); owner.Cursor = new Cursor(StandardCursorType.Arrow);
        e.Pointer.Capture(null); e.Handled = true;
        if (resize && Math.Abs(currentWidth - originalWidth) >= 0.1) commit(new(id, currentWidth));
        else if (moved) commit(new(id, beforeColumnId: target));
    }
    private void Cancel()
    {
        if (column != null && resizing) preview(new(column, originalWidth));
        column = null; resizing = dragging = false; insertion(null);
    }
}
