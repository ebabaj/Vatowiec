using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Metadata;
using Avalonia.Layout;
using Avalonia.Styling;
using Vatowiec.SharedContracts;

namespace Vatowiec.Desktop;

// A single header action, outside the data rows and horizontal scrolling area.
public sealed class ConfigurableTable : Grid
{
    public static readonly StyledProperty<TableColumn[]?> ColumnsProperty =
        AvaloniaProperty.Register<ConfigurableTable, TableColumn[]?>(nameof(Columns));
    public static readonly StyledProperty<Control?> RowsProperty =
        AvaloniaProperty.Register<ConfigurableTable, Control?>(nameof(Rows));
    public static readonly StyledProperty<string?> SettingsIdProperty =
        AvaloniaProperty.Register<ConfigurableTable, string?>(nameof(SettingsId));
    public static readonly StyledProperty<string?> SettingsLabelProperty =
        AvaloniaProperty.Register<ConfigurableTable, string?>(nameof(SettingsLabel));
    public static readonly StyledProperty<IBrush?> HeaderBackgroundProperty =
        AvaloniaProperty.Register<ConfigurableTable, IBrush?>(nameof(HeaderBackground));
    public static readonly StyledProperty<double> HeaderInsetProperty =
        AvaloniaProperty.Register<ConfigurableTable, double>(nameof(HeaderInset));

    private readonly TableCells cells = new();
    private readonly Button settings = new() { Content = "⚙", Width = 36, MinHeight = 32 };
    private readonly ScrollViewer heading = new()
    {
        HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden,
        VerticalScrollBarVisibility = ScrollBarVisibility.Disabled
    };
    private readonly ScrollViewer body = new()
    {
        HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
        VerticalScrollBarVisibility = ScrollBarVisibility.Disabled
    };
    private readonly Grid header;

    public TableColumn[]? Columns { get => GetValue(ColumnsProperty); set => SetValue(ColumnsProperty, value); }
    [Content]
    public Control? Rows { get => GetValue(RowsProperty); set => SetValue(RowsProperty, value); }
    public string? SettingsId { get => GetValue(SettingsIdProperty); set => SetValue(SettingsIdProperty, value); }
    public string? SettingsLabel { get => GetValue(SettingsLabelProperty); set => SetValue(SettingsLabelProperty, value); }
    public IBrush? HeaderBackground { get => GetValue(HeaderBackgroundProperty); set => SetValue(HeaderBackgroundProperty, value); }
    public double HeaderInset { get => GetValue(HeaderInsetProperty); set => SetValue(HeaderInsetProperty, value); }
    public event EventHandler<RoutedEventArgs>? ConfigureColumns;

    public ConfigurableTable()
    {
        RowDefinitions = new RowDefinitions("Auto,*");
        ColumnDefinitions = new ColumnDefinitions("*,36");
        heading.Content = cells;
        header = new Grid { ColumnDefinitions = new ColumnDefinitions("*,36") };
        header.Classes.Add("tableHeader");
        SetColumn(settings, 1);
        header.Children.Add(heading);
        header.Children.Add(settings);
        SetColumnSpan(header, 2);
        SetRow(body, 1);
        Children.Add(header);
        Children.Add(body);
        Styles.Add(new Style(x => x.OfType<ListBox>())
        {
            Setters = { new Setter(TemplatedControl.PaddingProperty, new Thickness(0)), new Setter(TemplatedControl.BorderThicknessProperty, new Thickness(0)) }
        });
        Styles.Add(new Style(x => x.OfType<ListBoxItem>())
        {
            Setters = { new Setter(TemplatedControl.PaddingProperty, new Thickness(0)), new Setter(ContentControl.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch) }
        });
        settings.Click += (_, e) => ConfigureColumns?.Invoke(this, e);
        body.ScrollChanged += (_, _) => heading.Offset = new Vector(body.Offset.X, 0);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ColumnsProperty) cells.Columns = Columns;
        if (change.Property == RowsProperty) body.Content = Rows;
        if (change.Property == HeaderBackgroundProperty) header.Background = HeaderBackground;
        if (change.Property == HeaderInsetProperty) cells.Margin = new Thickness(HeaderInset, 0, 0, 0);
        if (change.Property == SettingsIdProperty) AutomationProperties.SetAutomationId(settings, SettingsId);
        if (change.Property == SettingsLabelProperty)
        {
            AutomationProperties.SetName(settings, SettingsLabel);
            ToolTip.SetTip(settings, SettingsLabel);
        }
    }
}
