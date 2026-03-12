using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;
using RevitGeoExporter.Core.Models;
using RevitGeoExporter.Core.Preview;
using RevitGeoExporter.Export;

namespace RevitGeoExporter.UI;

public sealed class PreviewCanvasControl : Control
{
    private const float UnitFillOpacity = 200f;
    private const float SelectionPenWidth = 3f;
    private const float DefaultLineWidth = 2f;

    private IReadOnlyList<PreviewFeatureData> _features = Array.Empty<PreviewFeatureData>();
    private Bounds2D _bounds = Bounds2D.Empty;
    private ViewTransform2D _transform;
    private PreviewFeatureData? _selectedFeature;
    private PreviewFeatureData? _hoveredFeature;
    private bool _isPointerDown;
    private bool _isPanning;
    private Point _lastPointerLocation;
    private readonly ToolTip _toolTip = new();
    private readonly PreviewTileProvider _tileProvider;
    private PreviewMapContext? _mapContext;
    private PreviewBasemapSettings _basemapSettings = new(PreviewBasemapSettings.DefaultUrlTemplate, PreviewBasemapSettings.DefaultAttribution);
    private string _basemapStatusMessage = string.Empty;

    public PreviewCanvasControl()
    {
        DoubleBuffered = true;
        BackColor = Color.White;
        TabStop = true;
        _transform = new ViewTransform2D(1d, 1d, 0d, 0d, 1d);
        _tileProvider = new PreviewTileProvider(RequestCanvasInvalidate);
        _tileProvider.StatusMessageChanged += message => UpdateBasemapStatus(message);
    }

    public event Action<PreviewFeatureData?>? SelectedFeatureChanged;

    public event Action<string?>? BasemapStatusChanged;

    public bool ShowUnits { get; set; } = true;

    public bool ShowOpenings { get; set; } = true;

    public bool ShowDetails { get; set; } = true;

    public bool ShowLevels { get; set; } = true;

    public bool ShowStairs { get; set; } = true;

    public bool ShowEscalators { get; set; } = true;

    public bool ShowElevators { get; set; } = true;

    public bool ShowWarningsOnly { get; set; }

    public bool ShowOverriddenOnly { get; set; }

    public bool ShowUnassignedOnly { get; set; }

    public bool ShowBasemap { get; set; }

    public string SearchText { get; set; } = string.Empty;

    public bool BasemapAvailable =>
        _mapContext?.CanShowBasemap == true &&
        _basemapSettings.IsConfigured;

    public string BasemapAttribution => _basemapSettings.Attribution;

    public string BasemapUnavailableReason
    {
        get
        {
            if (!_basemapSettings.IsConfigured)
            {
                return "No basemap tile source is configured.";
            }

            return _mapContext?.UnavailableReason ?? string.Empty;
        }
    }

    public void SetViewData(IReadOnlyList<PreviewFeatureData> features, Bounds2D bounds)
    {
        _features = features ?? Array.Empty<PreviewFeatureData>();
        _bounds = bounds;
        _selectedFeature = null;
        ClearBasemapStatus();
        ResetView();
        NotifySelectionChanged();
    }

    public void ConfigureBasemap(PreviewMapContext? mapContext, PreviewBasemapSettings? basemapSettings)
    {
        _mapContext = mapContext;
        _basemapSettings = basemapSettings ?? new PreviewBasemapSettings(PreviewBasemapSettings.DefaultUrlTemplate, PreviewBasemapSettings.DefaultAttribution);
        ClearBasemapStatus();
        Invalidate();
    }

    public void FitToFeatures()
    {
        Bounds2D visibleBounds = GetVisibleBounds();
        _transform = ViewTransform2D.Fit(visibleBounds, ClientSize.Width, ClientSize.Height, 24d);
        Invalidate();
    }

    public void ResetView()
    {
        _transform = ViewTransform2D.Fit(_bounds, ClientSize.Width, ClientSize.Height, 24d);
        _selectedFeature = null;
        NotifySelectionChanged();
        Invalidate();
    }

    public void RefreshFilters()
    {
        if (_selectedFeature != null && !IsVisible(_selectedFeature))
        {
            _selectedFeature = null;
            NotifySelectionChanged();
        }

        Invalidate();
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        _transform = _transform.WithViewportSize(ClientSize.Width, ClientSize.Height);
        Invalidate();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        SetHoveredFeature(null);
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        double factor = e.Delta > 0 ? 1.15d : 1d / 1.15d;
        _transform = _transform.ZoomAt(factor, new Point2D(e.X, e.Y));
        Invalidate();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        Focus();
        if (e.Button != MouseButtons.Left)
        {
            return;
        }

        _isPointerDown = true;
        _isPanning = false;
        _lastPointerLocation = e.Location;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!_isPointerDown)
        {
            UpdateHoveredFeature(e.Location);
            return;
        }

        if (!_isPanning)
        {
            int deltaX = e.X - _lastPointerLocation.X;
            int deltaY = e.Y - _lastPointerLocation.Y;
            if ((deltaX * deltaX) + (deltaY * deltaY) >= 9)
            {
                _isPanning = true;
                Cursor = Cursors.Hand;
            }
        }

        if (_isPanning)
        {
            _transform = _transform.Pan(e.X - _lastPointerLocation.X, e.Y - _lastPointerLocation.Y);
            _lastPointerLocation = e.Location;
            Invalidate();
            return;
        }

        UpdateHoveredFeature(e.Location);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (e.Button != MouseButtons.Left)
        {
            return;
        }

        bool wasPanning = _isPanning;
        _isPointerDown = false;
        _isPanning = false;
        Cursor = Cursors.Default;

        if (!wasPanning)
        {
            SelectFeatureAt(e.Location);
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.Clear(BackColor);
        if (ShowBasemap && BasemapAvailable)
        {
            _tileProvider.DrawTiles(e.Graphics, _transform, _basemapSettings);
        }
        else
        {
            DrawGrid(e.Graphics);
        }

        List<PreviewFeatureData> visible = GetVisibleFeatures().ToList();
        if (visible.Count == 0)
        {
            DrawCenteredMessage(e.Graphics, "No preview features.");
            return;
        }

        foreach (PreviewFeatureData feature in visible)
        {
            if (!ReferenceEquals(feature, _selectedFeature))
            {
                DrawFeature(e.Graphics, feature, selected: false);
            }
        }

        if (_selectedFeature != null && visible.Contains(_selectedFeature))
        {
            DrawFeature(e.Graphics, _selectedFeature, selected: true);
        }
    }

    private IEnumerable<PreviewFeatureData> GetVisibleFeatures()
    {
        return _features.Where(IsVisible);
    }

    private bool IsVisible(PreviewFeatureData feature)
    {
        if (feature.FeatureType == ExportFeatureType.Unit && !ShowUnits)
        {
            return false;
        }

        if (feature.FeatureType == ExportFeatureType.Opening && !ShowOpenings)
        {
            return false;
        }

        if (feature.FeatureType == ExportFeatureType.Detail && !ShowDetails)
        {
            return false;
        }

        if (feature.FeatureType == ExportFeatureType.Level && !ShowLevels)
        {
            return false;
        }

        if (feature.FeatureType != ExportFeatureType.Unit)
        {
            return MatchesMetaFilters(feature);
        }

        string category = (feature.Category ?? string.Empty).Trim();
        if (category.Equals("stairs", StringComparison.OrdinalIgnoreCase))
        {
            return ShowStairs;
        }

        if (category.Equals("escalator", StringComparison.OrdinalIgnoreCase))
        {
            return ShowEscalators;
        }

        if (category.Equals("elevator", StringComparison.OrdinalIgnoreCase))
        {
            return ShowElevators;
        }

        return MatchesMetaFilters(feature);
    }

    private bool MatchesMetaFilters(PreviewFeatureData feature)
    {
        if (ShowWarningsOnly && !feature.HasWarning)
        {
            return false;
        }

        if (ShowOverriddenOnly && !feature.UsesFloorCategoryOverride)
        {
            return false;
        }

        if (ShowUnassignedOnly && !feature.IsUnassignedFloor)
        {
            return false;
        }

        string search = (SearchText ?? string.Empty).Trim();
        if (search.Length > 0 &&
            feature.SearchText.IndexOf(search, StringComparison.OrdinalIgnoreCase) < 0)
        {
            return false;
        }

        return true;
    }

    private Bounds2D GetVisibleBounds()
    {
        Bounds2D visibleBounds = FeatureBoundsCalculator.FromFeatures(GetVisibleFeatures().Select(x => x.Feature));
        return visibleBounds.IsEmpty ? _bounds : visibleBounds;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _tileProvider.Dispose();
            _toolTip.Dispose();
        }

        base.Dispose(disposing);
    }

    private void DrawGrid(Graphics graphics)
    {
        using Pen gridPen = new(Color.FromArgb(20, 0, 0, 0), 1f);
        const int spacing = 40;

        for (int x = spacing; x < ClientSize.Width; x += spacing)
        {
            graphics.DrawLine(gridPen, x, 0, x, ClientSize.Height);
        }

        for (int y = spacing; y < ClientSize.Height; y += spacing)
        {
            graphics.DrawLine(gridPen, 0, y, ClientSize.Width, y);
        }
    }

    private void DrawCenteredMessage(Graphics graphics, string message)
    {
        using StringFormat format = new()
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center,
        };
        using Brush brush = new SolidBrush(Color.Gray);
        graphics.DrawString(message, Font, brush, ClientRectangle, format);
    }

    private void DrawFeature(Graphics graphics, PreviewFeatureData feature, bool selected)
    {
        switch (feature.Feature)
        {
            case ExportPolygon polygon:
                DrawPolygonFeature(graphics, polygon, feature, selected);
                break;
            case ExportLineString lineString:
                DrawLineFeature(graphics, lineString, feature, selected);
                break;
        }
    }

    private void DrawPolygonFeature(Graphics graphics, ExportPolygon polygon, PreviewFeatureData feature, bool selected)
    {
        using GraphicsPath path = CreatePolygonPath(polygon);
        if (path.PointCount == 0)
        {
            return;
        }

        Color fillColor = ParseColor(feature.FillColorHex, Color.LightGray);
        int alpha = selected ? 230 : (int)UnitFillOpacity;
        using SolidBrush fillBrush = new(Color.FromArgb(alpha, fillColor));
        using Pen outlinePen = new(ParseColor(feature.StrokeColorHex, Color.DimGray), selected ? SelectionPenWidth : 1.5f);
        graphics.FillPath(fillBrush, path);
        graphics.DrawPath(outlinePen, path);
    }

    private void DrawLineFeature(Graphics graphics, ExportLineString lineString, PreviewFeatureData feature, bool selected)
    {
        PointF[] points = lineString.LineString.Points
            .Select(point => ToPointF(_transform.WorldToScreen(point)))
            .ToArray();
        if (points.Length < 2)
        {
            return;
        }

        using Pen pen = new(
            ParseColor(feature.StrokeColorHex, Color.DarkOrange),
            selected ? SelectionPenWidth : DefaultLineWidth)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
            LineJoin = LineJoin.Round,
        };
        graphics.DrawLines(pen, points);
    }

    private GraphicsPath CreatePolygonPath(ExportPolygon polygonFeature)
    {
        GraphicsPath path = new()
        {
            FillMode = FillMode.Alternate,
        };

        foreach (Polygon2D polygon in polygonFeature.Polygons)
        {
            AddRing(path, polygon.ExteriorRing);
            for (int i = 0; i < polygon.InteriorRings.Count; i++)
            {
                AddRing(path, polygon.InteriorRings[i]);
            }
        }

        return path;
    }

    private void AddRing(GraphicsPath path, IReadOnlyList<Point2D> ring)
    {
        PointF[] points = ring
            .Select(point => ToPointF(_transform.WorldToScreen(point)))
            .ToArray();
        if (points.Length >= 3)
        {
            path.AddPolygon(points);
        }
    }

    private void SelectFeatureAt(Point location)
    {
        List<PreviewFeatureData> visible = GetVisibleFeatures().ToList();
        Point2D worldPoint = _transform.ScreenToWorld(new Point2D(location.X, location.Y));
        double toleranceWorld = 8d / _transform.PixelsPerWorldUnit;
        int hitIndex = GeometryHitTester.FindHitIndex(visible, x => x.Feature, worldPoint, toleranceWorld);
        _selectedFeature = hitIndex >= 0 ? visible[hitIndex] : null;
        NotifySelectionChanged();
        Invalidate();
    }

    private void UpdateHoveredFeature(Point location)
    {
        List<PreviewFeatureData> visible = GetVisibleFeatures().ToList();
        Point2D worldPoint = _transform.ScreenToWorld(new Point2D(location.X, location.Y));
        double toleranceWorld = 8d / _transform.PixelsPerWorldUnit;
        int hitIndex = GeometryHitTester.FindHitIndex(visible, x => x.Feature, worldPoint, toleranceWorld);
        SetHoveredFeature(hitIndex >= 0 ? visible[hitIndex] : null);
    }

    private void SetHoveredFeature(PreviewFeatureData? feature)
    {
        if (ReferenceEquals(_hoveredFeature, feature))
        {
            return;
        }

        _hoveredFeature = feature;
        if (feature == null)
        {
            _toolTip.Hide(this);
            return;
        }

        string text = $"{feature.FeatureType} | {feature.Category ?? "-"} | {feature.ExportId ?? "-"}";
        _toolTip.SetToolTip(this, text);
    }

    private void NotifySelectionChanged()
    {
        SelectedFeatureChanged?.Invoke(_selectedFeature);
    }

    private void RequestCanvasInvalidate()
    {
        if (!IsHandleCreated)
        {
            return;
        }

        BeginInvoke(new Action(Invalidate));
    }

    private void UpdateBasemapStatus(string? message)
    {
        string normalized = (message ?? string.Empty).Trim();
        if (string.Equals(_basemapStatusMessage, normalized, StringComparison.Ordinal))
        {
            return;
        }

        _basemapStatusMessage = normalized;
        BasemapStatusChanged?.Invoke(_basemapStatusMessage.Length == 0 ? null : _basemapStatusMessage);
    }

    private void ClearBasemapStatus()
    {
        _tileProvider.ClearStatus();
        UpdateBasemapStatus(string.Empty);
    }

    private static PointF ToPointF(Point2D point)
    {
        return new PointF((float)point.X, (float)point.Y);
    }

    private static Color ParseColor(string hex, Color fallback)
    {
        string normalized = (hex ?? string.Empty).Trim().TrimStart('#');
        if (normalized.Length != 6)
        {
            return fallback;
        }

        try
        {
            int r = Convert.ToInt32(normalized.Substring(0, 2), 16);
            int g = Convert.ToInt32(normalized.Substring(2, 2), 16);
            int b = Convert.ToInt32(normalized.Substring(4, 2), 16);
            return Color.FromArgb(r, g, b);
        }
        catch
        {
            return fallback;
        }
    }
}

