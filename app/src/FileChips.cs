using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace CompInfo
{
    // Location chips (#даунлоадс #корзина …) in front of a note in a grid cell: «#Desktop #документы · 3».
    // Used by the ДСК document check and the document search.
    public class FileChips
    {
        public readonly List<string> Labels = new List<string>();
        public string Text = "";

        public static FileChips Parse(string value)
        {
            string rest = (value ?? "").Trim();
            FileChips chips = new FileChips();
            while (rest.StartsWith("#", StringComparison.Ordinal))
            {
                int space = rest.IndexOf(' ');
                string token = space < 0 ? rest : rest.Substring(0, space);
                chips.Labels.Add(token.Substring(1));
                rest = space < 0 ? "" : rest.Substring(space + 1).TrimStart();
            }
            if (chips.Labels.Count == 0) return null;
            if (rest.StartsWith("·", StringComparison.Ordinal)) rest = rest.Substring(1).TrimStart();
            chips.Text = rest;
            return chips;
        }

        // CellPainting handler body: draws the chips kept in the cell's Tag.
        public static void Paint(DataGridView grid, DataGridViewCellPaintingEventArgs e, int column)
        {
            if (e.RowIndex < 0 || e.ColumnIndex != column) return;
            FileChips chips = grid.Rows[e.RowIndex].Cells[e.ColumnIndex].Tag as FileChips;
            if (chips == null) return;
            e.PaintBackground(e.CellBounds, (e.State & DataGridViewElementStates.Selected) != 0);
            e.Paint(e.ClipBounds, DataGridViewPaintParts.Border);
            int em = e.CellStyle.Font.Height;
            int x = e.CellBounds.Left + em / 4;
            int top = e.CellBounds.Top + (e.CellBounds.Height - em - 2) / 2;
            using (Font chipFont = new Font("Segoe UI", 8.5f))
            {
                foreach (string label in chips.Labels)
                {
                    Size textSize = TextRenderer.MeasureText(e.Graphics, label, chipFont, Size.Empty,
                        TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
                    int width = textSize.Width + em * 2 / 3;
                    if (x + width > e.CellBounds.Right - em * 2) break;
                    Color fore, back;
                    Colors(label, out fore, out back);
                    Rectangle chip = new Rectangle(x, top, width, em + 2);
                    e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                    using (System.Drawing.Drawing2D.GraphicsPath shape = Shapes.Round(chip, chip.Height / 2))
                    using (SolidBrush brush = new SolidBrush(back)) e.Graphics.FillPath(brush, shape);
                    TextRenderer.DrawText(e.Graphics, label, chipFont, chip, fore,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
                    x += width + em / 4;
                }
            }
            Color textColor = (e.State & DataGridViewElementStates.Selected) != 0 ? e.CellStyle.SelectionForeColor : e.CellStyle.ForeColor;
            TextRenderer.DrawText(e.Graphics, chips.Text, e.CellStyle.Font,
                new Rectangle(x, e.CellBounds.Top, Math.Max(0, e.CellBounds.Right - x - em / 4), e.CellBounds.Height), textColor,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            e.Handled = true;
        }

        static BadgeKind Kind(string label)
        {
            if (label == L.T("#корзина").TrimStart('#')) return BadgeKind.Danger;
            if (label == L.T("#даунлоадс").TrimStart('#')) return BadgeKind.Warn;
            if (label == "WhatsApp") return BadgeKind.Ok;
            if (label == "Desktop") return BadgeKind.Muted;
            return BadgeKind.Ok;
        }

        static void Colors(string label, out Color fore, out Color back)
        {
            if (label == "Claude")
            {
                fore = Theme.Dark ? Color.FromArgb(232, 154, 125) : Color.FromArgb(166, 75, 49);
                back = Theme.Dark ? Color.FromArgb(67, 42, 36) : Color.FromArgb(247, 229, 221);
                return;
            }
            Badges.Colors(Kind(label), out fore, out back);
        }
    }
}
