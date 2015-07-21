using System.Globalization;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Drawing.Text;
using System.Text.RegularExpressions;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace ezBet2
{
    public class MatchListBox : ListBox
    {
        private Font fontTeamName;
        private Font fontHeader;
        private Font bolded_font;
        private Font cur_font;

        private int imageWidth;
        private int imageHeight;
        private int itemWidth;
        private int itemHeight;
        private int teamATextPlacementW;
        private int teamBTextPlacementW;
        private int teamTextPlacementH;

        private int teamAImagePlacementW;
        private int teamBImagePlacementW;
        private int teamImagePlacementH;

        public MatchListBox()
        {
            this.DoubleBuffered = true;
            this.DrawMode = DrawMode.OwnerDrawFixed; // We're using custom drawing.
            bolded_font = new Font("Franklin Gothic Medium Cond", 12);
            fontTeamName = new Font("Franklin Gothic Book", 12);
            fontHeader = new Font("Franklin Gothic Book", 8);
            cur_font = fontTeamName;
        }

        [DllImport("Gdi32.dll", EntryPoint = "CreateRoundRectRgn")]
        private static extern IntPtr CreateRoundRectRgn
        (
            int nLeftRect, // x-coordinate of upper-left corner
            int nTopRect, // y-coordinate of upper-left corner
            int nRightRect, // x-coordinate of lower-right corner
            int nBottomRect, // y-coordinate of lower-right corner
            int nWidthEllipse, // height of ellipse
            int nHeightEllipse // width of ellipse
         );

        protected override void OnResize(EventArgs e)
        {
            Region = System.Drawing.Region.FromHrgn(CreateRoundRectRgn(0, 0, Width + 1, Height + 1, 15, 15));
            base.OnResize(e);
        }

        protected override void OnDrawItem(DrawItemEventArgs e)
        {
            // Make sure we're not trying to draw something that isn't there.
            if (e.Index >= this.Items.Count || e.Index <= -1)
                return;

            // Get the item object.
            Match match = (Match)this.Items[e.Index];
            Color backgroundColor;
            Color textColor;
            String teamAPicName;
            String teamBPicName;
            Bitmap t1;
            Bitmap t2;

            imageWidth = 40;
            imageHeight = 40;
            itemWidth = e.Bounds.Width;
            itemHeight = e.Bounds.Height;
            teamATextPlacementW = (int)(e.Bounds.Width / 5.175);
            teamBTextPlacementW = (int)(e.Bounds.Width - e.Bounds.Width / 5.175);
            teamTextPlacementH = (int)(e.Bounds.Y + e.Bounds.Height / 2.55);
            
            teamAImagePlacementW = (int)(e.Bounds.X + e.Bounds.Width / 2.96);
            teamBImagePlacementW = (int)(e.Bounds.Width - e.Bounds.Width / 2.96 - imageWidth);
            teamImagePlacementH = (int)(e.Bounds.Y + (e.Bounds.Height - imageHeight)/2 + 8);

            // Draw the background color depending on 
            // if the item is selected or not.
            if ((e.State & DrawItemState.Selected) == DrawItemState.Selected)
            {
                // The item is selected.
                backgroundColor = Color.FromArgb(64, 64, 64);
                textColor = Color.DarkOrange;
                teamAPicName = match.team_a.name;// +"_D";
                teamBPicName = match.team_b.name;// +"_D";

                t1 = new Bitmap(ezBet2.Properties.Resources.NotFound);
                t2 = new Bitmap(ezBet2.Properties.Resources.NotFound);
            }
            else
            {
                // The item is NOT selected.
                backgroundColor = Color.FromArgb(187, 187, 187);
                textColor = Color.FromArgb(64, 64, 64);
                teamAPicName = match.team_a.name;
                teamBPicName = match.team_b.name;

                t1 = new Bitmap(ezBet2.Properties.Resources.NotFound);
                t2 = new Bitmap(ezBet2.Properties.Resources.NotFound);
            }

            e.Graphics.FillRectangle(new SolidBrush(this.BackColor), e.Bounds);

            DrawRoundedRectangle(e.Graphics, new Rectangle(new Point(e.Bounds.X + 10, e.Bounds.Y + 20), new Size(e.Bounds.Width - 20, e.Bounds.Height - 25)), 10, new Pen(backgroundColor, 3), backgroundColor);
            
            // Draw the item.
            
            FindImage(teamAPicName, ref t1);
            FindImage(teamBPicName, ref t2);

            StringFormat sf = new StringFormat();
            sf.Alignment = StringAlignment.Center;

            double ta = match.team_a_pct * 100;
            double tb = match.team_b_pct * 100;

            int hours = (match.match_time - DateTime.Now).Hours;
            int minutes = (match.match_time - DateTime.Now).Minutes;
            string timeString = String.Empty;

            if (minutes > 0)
            {
                // game has not started
                timeString = (hours == 0) ? (minutes.ToString() + " minutes from now") : (hours.ToString() + " hours from now");
            }
            else
            {
                // game started
                timeString = "LIVE!";
            }

            string format_string = String.Empty;
            switch (match.format)
            {
                case (MatchFormat.BestOf1):
                    format_string = "Best of 1";
                    break;
                case (MatchFormat.BestOf2):
                    format_string = "Best of 2";
                    break;
                case (MatchFormat.BestOf3):
                    format_string = "Best of 3";
                    break;
                case (MatchFormat.BestOf5):
                    format_string = "Best of 5";
                    break;
                default:
                    format_string = "Unknown Format";
                    break;
            }

            // header - Time | Format
            e.Graphics.DrawString(timeString, fontHeader, new SolidBrush(Color.FromArgb(64, 64, 64)), new PointF(7, e.Bounds.Y + 3));
            // header - Format
            e.Graphics.DrawString(format_string, fontHeader, new SolidBrush(Color.FromArgb(64, 64, 64)), new PointF(e.Bounds.Width / 2, e.Bounds.Y + 3), sf);
            // header - Event
            e.Graphics.DrawString(match.match_event.ToString(), fontHeader, new SolidBrush(Color.FromArgb(64, 64, 64)),
                new PointF(e.Bounds.Width - 7, e.Bounds.Y + 3), new StringFormat(StringFormatFlags.DirectionRightToLeft));

            // Team A name
            if (match.rec_bet_a)
                cur_font = bolded_font;
            e.Graphics.DrawString(match.team_a.name, cur_font, new SolidBrush(textColor),
                new PointF(teamATextPlacementW, teamTextPlacementH), sf);
            cur_font = fontTeamName;

            // Bet indicator
            int outOf255 = 255 - (int)(match.rec_bet_pct * (match.rec_bet_a ? match.team_a_odds : match.team_b_odds) * 255);
            if (outOf255 < 0)
                outOf255 = 0;

            e.Graphics.FillEllipse(new SolidBrush(Color.FromArgb(255, outOf255, 0)), e.Bounds.X + 20, teamTextPlacementH - 5, 10, 10);
            e.Graphics.DrawEllipse(new Pen(Color.Black), e.Bounds.X + 20, teamTextPlacementH - 5, 10, 10);

            // Team A odds
            e.Graphics.DrawString(String.Format("(" + Math.Truncate(ta) + "%, "
                + (Math.Truncate(match.team_a_odds * 100) / 100).ToString("#0.00", CultureInfo.InvariantCulture) + ")"),
                this.Font, new SolidBrush(textColor),
                new PointF(teamATextPlacementW, teamTextPlacementH + 16), sf);

            // Team A icon
            e.Graphics.DrawImage(t1, teamAImagePlacementW, teamImagePlacementH, imageWidth, imageHeight);

            if (match.rec_bet_a && (e.State & DrawItemState.Selected) == DrawItemState.Selected)
                DrawRoundedRectangle(e.Graphics, new Rectangle(new Point(teamAImagePlacementW - 4, teamImagePlacementH - 4), new Size(imageWidth + 8, imageHeight + 8)), 10, new Pen(Color.DarkOrange, 3), Color.Transparent);
            else
                DrawRoundedRectangle(e.Graphics, new Rectangle(new Point(teamAImagePlacementW - 4, teamImagePlacementH - 4), new Size(imageWidth + 8, imageHeight + 8)), 10, new Pen(Color.FromArgb(187, 187, 187), 3), Color.Transparent);

            // VS
            e.Graphics.DrawString("VS", this.Font, new SolidBrush(textColor),
                new PointF(e.Bounds.Width / 2, teamTextPlacementH + 8), sf);

            // Team B icon
            e.Graphics.DrawImage(t2, teamBImagePlacementW, teamImagePlacementH, imageWidth, imageHeight);

            if (!match.rec_bet_a && (e.State & DrawItemState.Selected) == DrawItemState.Selected)
                DrawRoundedRectangle(e.Graphics, new Rectangle(new Point(teamBImagePlacementW - 4, teamImagePlacementH - 4), new Size(imageWidth + 8, imageHeight + 8)), 10, new Pen(Color.DarkOrange, 3), Color.Transparent);
            else
                DrawRoundedRectangle(e.Graphics, new Rectangle(new Point(teamBImagePlacementW - 4, teamImagePlacementH - 4), new Size(imageWidth + 8, imageHeight + 8)), 10, new Pen(Color.FromArgb(187, 187, 187), 3), Color.Transparent);

            // Team B name
            if (!match.rec_bet_a)
                cur_font = bolded_font;
            e.Graphics.DrawString(match.team_b.name, cur_font, new SolidBrush(textColor),
                new PointF(teamBTextPlacementW, teamTextPlacementH), sf);
            cur_font = fontTeamName;

            // Team B odds
            e.Graphics.DrawString(
                String.Format("(" + Math.Truncate(tb) + "%, "
                + (Math.Truncate(match.team_b_odds * 100) / 100).ToString("#0.00", CultureInfo.InvariantCulture) + ")"),
                this.Font,
                new SolidBrush(textColor),
                new PointF(teamBTextPlacementW, teamTextPlacementH + 16), sf);
        }

        private void FindImage(String text, ref Bitmap i)
        {
            String converted = text;
            converted = converted.Replace('.', '_');
            converted = converted.Replace(' ', '_');
            converted = converted.Replace('\'', '_');
            if (converted.Length > 0 && char.IsDigit(converted[0]))
                converted = "_" + converted;

            try
            {
                i = new Bitmap((Bitmap)ezBet2.Properties.Resources.ResourceManager.GetObject(converted));
            }
            catch (Exception)
            {
                // image is already ? mark, don't need to set it. 
            }
        }

        private void InitializeComponent()
        {
            this.SuspendLayout();
            this.ResumeLayout(false);
        }

        protected override void OnNotifyMessage(Message m)
        {
            //Filter out the WM_ERASEBKGND message
            if (m.Msg != 0x14)
            {
                base.OnNotifyMessage(m);
            }
        }

        private void DrawRoundedRectangle(Graphics gfx, Rectangle Bounds, int CornerRadius, Pen DrawPen, Color FillColor)
        {
            int strokeOffset = Convert.ToInt32(Math.Ceiling(DrawPen.Width));
            Bounds = Rectangle.Inflate(Bounds, -strokeOffset, -strokeOffset);

            DrawPen.EndCap = DrawPen.StartCap = LineCap.Round;

            GraphicsPath gfxPath = new GraphicsPath();
            gfxPath.AddArc(Bounds.X, Bounds.Y, CornerRadius, CornerRadius, 180, 90);
            gfxPath.AddArc(Bounds.X + Bounds.Width - CornerRadius, Bounds.Y, CornerRadius, CornerRadius, 270, 90);
            gfxPath.AddArc(Bounds.X + Bounds.Width - CornerRadius, Bounds.Y + Bounds.Height - CornerRadius, CornerRadius, CornerRadius, 0, 90);
            gfxPath.AddArc(Bounds.X, Bounds.Y + Bounds.Height - CornerRadius, CornerRadius, CornerRadius, 90, 90);
            gfxPath.CloseAllFigures();

            gfx.FillPath(new SolidBrush(FillColor), gfxPath);
            gfx.DrawPath(DrawPen, gfxPath);
        }
    }
}
