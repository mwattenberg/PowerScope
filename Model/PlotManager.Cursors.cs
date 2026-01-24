using System;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using ScottPlot.Plottables;
using Color = System.Windows.Media.Color;

namespace PowerScope.Model
{
    /// <summary>
    /// PlotManager partial class - Cursor functionality
    /// Handles all cursor-related operations including creation, removal, and mouse interaction
    /// </summary>
    public partial class PlotManager
    {
        // Cursor fields
        private readonly Cursor _cursor;
        private HorizontalLine _horizontalCursorA;
        private HorizontalLine _horizontalCursorB;
        private VerticalLine _verticalCursorA;
        private VerticalLine _verticalCursorB;
        private AxisLine _plottableBeingDragged;
        private bool _cursorMouseHandlingEnabled;

        #region Cursor Properties

        /// <summary>
        /// The cursor instance owned by this PlotManager
        /// </summary>
        public Cursor Cursor
        {
            get { return _cursor; }
        }

        /// <summary>
        /// Whether cursors are currently active
        /// </summary>
        public bool HasActiveCursors { get; private set; }

        /// <summary>
        /// Current active cursor mode
        /// </summary>
        public CursorMode ActiveCursorMode { get; private set; }

        #endregion

        #region Cursor Public Methods

        /// <summary>
        /// Enables vertical cursors - no parameter needed since PlotManager owns the cursor
        /// </summary>
        public void EnableVerticalCursors()
        {
            DisableCursors(); // Remove any existing cursors
            CreateVerticalCursors();
            SetupCursorMouseHandling();
            ActiveCursorMode = CursorMode.Vertical;
            HasActiveCursors = true;
            _cursor.ActiveMode = CursorMode.Vertical;
            UpdateVerticalCursorData();
            _plot.Refresh();
            OnPropertyChanged(nameof(HasActiveCursors));
            OnPropertyChanged(nameof(ActiveCursorMode));
        }

        /// <summary>
        /// Enables horizontal cursors - no parameter needed since PlotManager owns the cursor
        /// </summary>
        public void EnableHorizontalCursors()
        {
            DisableCursors(); // Remove any existing cursors
            CreateHorizontalCursors();
            SetupCursorMouseHandling();
            ActiveCursorMode = CursorMode.Horizontal;
            HasActiveCursors = true;
            _cursor.ActiveMode = CursorMode.Horizontal;
            UpdateHorizontalCursorData();
            _plot.Refresh();
            OnPropertyChanged(nameof(HasActiveCursors));
            OnPropertyChanged(nameof(ActiveCursorMode));
        }

        /// <summary>
        /// Disables all cursors and cleans up cursor state
        /// </summary>
        public void DisableCursors()
        {
            RemoveAllCursor();

            // Remove mouse handling only if trigger line is not visible
            if (!_triggerLineVisible && _cursorMouseHandlingEnabled)
            {
                RemoveCursorMouseHandling();
            }

            ActiveCursorMode = CursorMode.None;
            HasActiveCursors = false;
            _cursor.ActiveMode = CursorMode.None;
            _plot.Refresh();
            OnPropertyChanged(nameof(HasActiveCursors));
            OnPropertyChanged(nameof(ActiveCursorMode));
        }

        /// <summary>
        /// Updates cursor channel values using the unified cursor view model
        /// This centralizes cursor data retrieval and reduces coupling
        /// </summary>
        /// <param name="cursor">The cursor model to update</param>
        public void UpdateCursorValues(Cursor cursor)
        {
            if (cursor == null)
                return;

            cursor.UpdateChannelValues(this);
        }

        #endregion

        #region Cursor Private Methods

        private void CreateVerticalCursors()
        {
            // Place lines at 25% and 75% of current x-axis span
            var xAxisRange = _plot.Plot.Axes.GetXAxes().First().Range;
            double x1 = xAxisRange.Min + (xAxisRange.Max - xAxisRange.Min) * 0.25;
            double x2 = xAxisRange.Min + (xAxisRange.Max - xAxisRange.Min) * 0.75;

            // Get the highlight colors from App.xaml resources
            var highlightNormal = (Color)Application.Current.Resources["Highlight_Normal"];
            var highlightComplementary = (Color)Application.Current.Resources["Highlight_Complementary"];

            _verticalCursorA = _plot.Plot.Add.VerticalLine(x1);
            _verticalCursorA.IsDraggable = true;
            _verticalCursorA.Text = "A";
            _verticalCursorA.Color = new ScottPlot.Color(highlightNormal.R, highlightNormal.G, highlightNormal.B);

            _verticalCursorB = _plot.Plot.Add.VerticalLine(x2);
            _verticalCursorB.IsDraggable = true;
            _verticalCursorB.Text = "B";
            _verticalCursorB.Color = new ScottPlot.Color(highlightComplementary.R, highlightComplementary.G, highlightComplementary.B);
        }

        private void CreateHorizontalCursors()
        {
            // Place lines at 25% and 75% of current y-axis span
            var limits = _plot.Plot.Axes.GetYAxes().First().Range;
            double y1 = limits.Min + (limits.Max - limits.Min) * 0.25;
            double y2 = limits.Min + (limits.Max - limits.Min) * 0.75;

            // Get the highlight colors from App.xaml resources
            var highlightNormal = (Color)Application.Current.Resources["Highlight_Normal"];
            var highlightComplementary = (Color)Application.Current.Resources["Highlight_Complementary"];

            _horizontalCursorA = _plot.Plot.Add.HorizontalLine(y1);
            _horizontalCursorA.IsDraggable = true;
            _horizontalCursorA.Text = "A";
            _horizontalCursorA.Color = new ScottPlot.Color(highlightNormal.R, highlightNormal.G, highlightNormal.B);

            _horizontalCursorB = _plot.Plot.Add.HorizontalLine(y2);
            _horizontalCursorB.IsDraggable = true;
            _horizontalCursorB.Text = "B";
            _horizontalCursorB.Color = new ScottPlot.Color(highlightComplementary.R, highlightComplementary.G, highlightComplementary.B);
        }

        private void RemoveAllCursor()
        {
            RemoveVerticalCursor();
            RemoveHorizontalCursor();
        }

        private void RemoveVerticalCursor()
        {
            if (_verticalCursorA != null)
            {
                _plot.Plot.Remove(_verticalCursorA);
                _verticalCursorA = null;
            }
            if (_verticalCursorB != null)
            {
                _plot.Plot.Remove(_verticalCursorB);
                _verticalCursorB = null;
            }
        }

        private void RemoveHorizontalCursor()
        {
            if (_horizontalCursorA != null)
            {
                _plot.Plot.Remove(_horizontalCursorA);
                _horizontalCursorA = null;
            }
            if (_horizontalCursorB != null)
            {
                _plot.Plot.Remove(_horizontalCursorB);
                _horizontalCursorB = null;
            }
        }

        /// <summary>
        /// Updates the cursor model with current vertical cursor positions
        /// </summary>
        private void UpdateVerticalCursorData()
        {
            if (_verticalCursorA == null || _verticalCursorB == null)
                return;

            double cursorASample = _verticalCursorA.X;
            double cursorBSample = _verticalCursorB.X;
            double sampleRate = _channels[0].OwnerStream.SampleRate;

            _cursor.UpdateVerticalCursors(cursorASample, cursorBSample, sampleRate);

            // Update channel values directly - much simpler since we have direct access
            _cursor.UpdateChannelValues(this);
        }

        /// <summary>
        /// Updates the cursor model with current horizontal cursor positions
        /// </summary>
        private void UpdateHorizontalCursorData()
        {
            if (_horizontalCursorA == null || _horizontalCursorB == null)
                return;

            double cursorAYValue = _horizontalCursorA.Y;
            double cursorBYValue = _horizontalCursorB.Y;

            _cursor.UpdateHorizontalCursors(cursorAYValue, cursorBYValue);
        }

        #endregion

        #region Mouse Handling

        private void SetupCursorMouseHandling()
        {
            if (!_cursorMouseHandlingEnabled)
            {
                _plot.MouseDown += Plot_MouseDown;
                _plot.MouseUp += Plot_MouseUp;
                _plot.MouseMove += Plot_MouseMove;
                _cursorMouseHandlingEnabled = true;
            }
        }

        private void RemoveCursorMouseHandling()
        {
            if (_cursorMouseHandlingEnabled)
            {
                _plot.MouseDown -= Plot_MouseDown;
                _plot.MouseUp -= Plot_MouseUp;
                _plot.MouseMove -= Plot_MouseMove;
                _cursorMouseHandlingEnabled = false;
            }
        }

        private void Plot_MouseDown(object sender, MouseButtonEventArgs e)
        {
            Point pos = e.GetPosition(_plot);
            AxisLine line = GetLineUnderMouse((float)pos.X, (float)pos.Y);
            if (line != null)
            {
                _plottableBeingDragged = line;
                _plot.UserInputProcessor.Disable();
                e.Handled = true;
            }
        }

        private void Plot_MouseUp(object sender, MouseButtonEventArgs e)
        {
            _plottableBeingDragged = null;
            _plot.UserInputProcessor.Enable();
            _plot.Refresh();
            Mouse.OverrideCursor = null;
        }

        private void Plot_MouseMove(object sender, MouseEventArgs e)
        {
            Point pos = e.GetPosition(_plot);
            var rect = _plot.Plot.GetCoordinateRect((float)pos.X, (float)pos.Y, radius: 10);

            if (_plottableBeingDragged == null)
            {
                AxisLine lineUnderMouse = GetLineUnderMouse((float)(pos.X), (float)(pos.Y));
                if (lineUnderMouse == null)
                    Mouse.OverrideCursor = null;
                else if (lineUnderMouse.IsDraggable && lineUnderMouse is VerticalLine)
                    Mouse.OverrideCursor = Cursors.SizeWE;
                else if (lineUnderMouse.IsDraggable && lineUnderMouse is HorizontalLine)
                    Mouse.OverrideCursor = Cursors.SizeNS;
            }
            else
            {
                if (_plottableBeingDragged is HorizontalLine horizontalLine)
                {
                    horizontalLine.Y = rect.VerticalCenter * _dpi.DpiScaleX;

                    // Check if this is the trigger line - delegate to trigger partial class
                    if (IsTriggerLine(horizontalLine))
                    {
                        HandleTriggerLevelDrag(horizontalLine);
                    }
                    else
                    {
                        // Regular cursor line
                        horizontalLine.Text = $"{horizontalLine.Y:0.0}";
                        UpdateHorizontalCursorData();
                    }
                }
                else if (_plottableBeingDragged is VerticalLine verticalLine)
                {
                    verticalLine.X = rect.HorizontalCenter * _dpi.DpiScaleY;

                    // Check if this is the trigger position line - delegate to trigger partial class
                    if (IsTriggerLine(verticalLine))
                    {
                        HandleTriggerPositionDrag(verticalLine);
                    }
                    else
                    {
                        // Regular cursor line
                        verticalLine.Text = $"{verticalLine.X:0}";
                        UpdateVerticalCursorData();
                    }
                }
                _plot.Refresh();
                e.Handled = true;
            }
        }

        private AxisLine GetLineUnderMouse(float x, float y)
        {
            var rect = _plot.Plot.GetCoordinateRect((float)(x * _dpi.DpiScaleX), (float)(y * _dpi.DpiScaleY), radius: 10);
            foreach (AxisLine axLine in _plot.Plot.GetPlottables<AxisLine>().Reverse())
            {
                if (axLine.IsUnderMouse(rect))
                    return axLine;
            }
            return null;
        }

        #endregion
    }
}
