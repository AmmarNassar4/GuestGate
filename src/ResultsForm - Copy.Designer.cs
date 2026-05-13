using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;

namespace GuestGate.Desktop
{
    partial class ResultsForm
    {
        private IContainer components = null;

        private Panel _scrollHost;
        private TableLayoutPanel _table;
        private Label _titleInfo;

        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
                components.Dispose();
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code
        private void InitializeComponent()
        {
            this._titleInfo = new System.Windows.Forms.Label();
            this._scrollHost = new System.Windows.Forms.Panel();
            this._table = new System.Windows.Forms.TableLayoutPanel();
            this._scrollHost.SuspendLayout();
            this.SuspendLayout();
            // 
            // _titleInfo
            // 
            this._titleInfo.Dock = System.Windows.Forms.DockStyle.Top;
            this._titleInfo.Location = new System.Drawing.Point(0, 0);
            this._titleInfo.Name = "_titleInfo";
            this._titleInfo.Padding = new System.Windows.Forms.Padding(10, 0, 0, 0);
            this._titleInfo.Size = new System.Drawing.Size(349, 24);
            this._titleInfo.TabIndex = 1;
            this._titleInfo.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // _scrollHost
            // 
            this._scrollHost.AutoScroll = true;
            this._scrollHost.Controls.Add(this._table);
            this._scrollHost.Dock = System.Windows.Forms.DockStyle.Fill;
            this._scrollHost.Location = new System.Drawing.Point(0, 24);
            this._scrollHost.Name = "_scrollHost";
            this._scrollHost.Size = new System.Drawing.Size(349, 456);
            this._scrollHost.TabIndex = 0;
            // 
            // _table
            // 
            this._table.AutoSize = true;
            this._table.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
            this._table.ColumnCount = 3;
            this._table.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle());
            this._table.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
            this._table.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle());
            this._table.Dock = System.Windows.Forms.DockStyle.Top;
            this._table.Location = new System.Drawing.Point(0, 0);
            this._table.Margin = new System.Windows.Forms.Padding(0);
            this._table.Name = "_table";
            this._table.Padding = new System.Windows.Forms.Padding(10);
            this._table.RowCount = 1;
            this._table.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 20F));
            this._table.Size = new System.Drawing.Size(349, 40);
            this._table.TabIndex = 0;
            // 
            // ResultsForm
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(349, 480);
            this.Controls.Add(this._scrollHost);
            this.Controls.Add(this._titleInfo);
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedToolWindow;
            this.Name = "ResultsForm";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            this.Text = "Guest Result";
            this.TopMost = true;
            this._scrollHost.ResumeLayout(false);
            this._scrollHost.PerformLayout();
            this.ResumeLayout(false);

        }
        #endregion
    }
}
