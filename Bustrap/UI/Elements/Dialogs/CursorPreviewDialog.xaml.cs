using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Bustrap.Enums;
using Bustrap.UI.Elements.Base;

namespace Bustrap.UI.Elements.Dialogs
{
    /// <summary>
    /// Dialog for previewing cursor types before applying them
    /// </summary>
    public partial class CursorPreviewDialog : WpfUiWindow
    {
        public Bustrap.Enums.CursorType? SelectedCursor { get; private set; }

        public CursorPreviewDialog()
        {
            InitializeComponent();
            LoadCursorPreviews();
        }

        private void LoadCursorPreviews()
        {
            var cursors = new[]
            {
                Bustrap.Enums.CursorType.Default,
                Bustrap.Enums.CursorType.FPSCursor,
                Bustrap.Enums.CursorType.CleanCursor,
                Bustrap.Enums.CursorType.DotCursor,
                Bustrap.Enums.CursorType.StoofsCursor,
                Bustrap.Enums.CursorType.From2006,
                Bustrap.Enums.CursorType.From2013,
                Bustrap.Enums.CursorType.WhiteDotCursor,
                Bustrap.Enums.CursorType.VerySmallWhiteDot
            };

            foreach (var cursor in cursors)
            {
                var previewItem = CreateCursorPreviewItem(cursor);
                CursorStackPanel.Children.Add(previewItem);
            }
        }

        private FrameworkElement CreateCursorPreviewItem(Bustrap.Enums.CursorType cursor)
        {
            var border = new System.Windows.Controls.Border
            {
                BorderBrush = new SolidColorBrush(Colors.Gray),
                BorderThickness = new Thickness(1),
                Margin = new Thickness(5),
                Padding = new Thickness(10),
                Background = new SolidColorBrush(Colors.Transparent),
                Cursor = System.Windows.Input.Cursors.Hand
            };

            var stackPanel = new System.Windows.Controls.StackPanel
            {
                Orientation = System.Windows.Controls.Orientation.Horizontal
            };

            // Load cursor image for preview
            var image = new System.Windows.Controls.Image
            {
                Width = 32,
                Height = 32,
                Margin = new Thickness(0, 0, 10, 0)
            };

            try
            {
                var imagePath = GetCursorImagePath(cursor);
                if (!string.IsNullOrEmpty(imagePath))
                {
                    var uri = new Uri($"pack://application:,,,/Resources/Mods/{imagePath}");
                    image.Source = new BitmapImage(uri);
                }
            }
            catch
            {
                // Use default image if cursor image can't be loaded
                image.Source = null;
            }

            var nameLabel = new System.Windows.Controls.TextBlock
            {
                Text = GetCursorDisplayName(cursor),
                VerticalAlignment = VerticalAlignment.Center,
                FontSize = 14
            };

            stackPanel.Children.Add(image);
            stackPanel.Children.Add(nameLabel);
            border.Child = stackPanel;

            border.MouseLeftButtonUp += (s, e) =>
            {
                SelectedCursor = cursor;
                DialogResult = true;
                Close();
            };

            border.MouseEnter += (s, e) =>
            {
                border.Background = new SolidColorBrush(Color.FromArgb(50, 100, 149, 237));
            };

            border.MouseLeave += (s, e) =>
            {
                border.Background = new SolidColorBrush(Colors.Transparent);
            };

            return border;
        }

        private string GetCursorImagePath(Bustrap.Enums.CursorType cursor)
        {
            return cursor switch
            {
                Bustrap.Enums.CursorType.FPSCursor => "Cursor/FPSCursor/ArrowCursor.png",
                Bustrap.Enums.CursorType.CleanCursor => "Cursor/CleanCursor/ArrowCursor.png",
                Bustrap.Enums.CursorType.DotCursor => "Cursor/DotCursor/ArrowCursor.png",
                Bustrap.Enums.CursorType.StoofsCursor => "Cursor/StoofsCursor/ArrowCursor.png",
                Bustrap.Enums.CursorType.From2006 => "Cursor/From2006/ArrowCursor.png",
                Bustrap.Enums.CursorType.From2013 => "Cursor/From2013/ArrowCursor.png",
                Bustrap.Enums.CursorType.WhiteDotCursor => "Cursor/WhiteDotCursor/ArrowCursor.png",
                Bustrap.Enums.CursorType.VerySmallWhiteDot => "Cursor/VerySmallWhiteDot/ArrowCursor.png",
                _ => string.Empty
            };
        }

        private string GetCursorDisplayName(Bustrap.Enums.CursorType cursor)
        {
            return cursor switch
            {
                Bustrap.Enums.CursorType.Default => "Default",
                Bustrap.Enums.CursorType.FPSCursor => "FPS Cursor (V1)",
                Bustrap.Enums.CursorType.CleanCursor => "Clean Cursor",
                Bustrap.Enums.CursorType.DotCursor => "Dot Cursor",
                Bustrap.Enums.CursorType.StoofsCursor => "Stoofs Cursor",
                Bustrap.Enums.CursorType.From2006 => "2006 Legacy Cursor",
                Bustrap.Enums.CursorType.From2013 => "2013 Legacy Cursor",
                Bustrap.Enums.CursorType.WhiteDotCursor => "White Dot Cursor",
                Bustrap.Enums.CursorType.VerySmallWhiteDot => "Very Small White Dot",
                _ => cursor.ToString()
            };
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}