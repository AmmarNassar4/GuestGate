using System;
using System.ComponentModel;
using System.Windows.Forms;
using System.Drawing;

namespace GuestGate.Desktop
{
    partial class ReceptionForm
    {
        private IContainer components = null;

        private Panel _top;
        private ComboBox _kidBox;
        private ComboBox _templateBox;
        private Button _startBtn;
        private Button _endBtn;
        private Label lblKid;
        private Label lblTemplate;

        private Panel _prefillHost;
        private TableLayoutPanel _prefillTable;

        private StatusStrip _status;
        private ToolStripStatusLabel _hubLbl;
        private ToolStripStatusLabel toolStripSeparator1;
        private ToolStripStatusLabel _sessionLbl;
        private ToolStripStatusLabel toolStripSeparator2;
        private ToolStripStatusLabel _templateLbl;
        private ToolStripStatusLabel toolStripSeparator3;
        private ToolStripStatusLabel _msgLbl;

        private Timer _retryTimer;

        // Legacy consent controls kept for the old SendConsentRequestAsync method.
        // The active consent flow now opens ConsentRequestForm from the launcher button.
        private ComboBox _consentLanguage = new ComboBox();
        private TextBox _consentGuestName = new TextBox();
        private TextBox _consentTermsEn = new TextBox();
        private TextBox _consentTermsAr = new TextBox();

        /// <summary>
        /// Clean up any resources being used.
        /// </summary>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
                components.Dispose();
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code
        private void InitializeComponent()
        {
            this.components = new System.ComponentModel.Container();
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(ReceptionForm));
            this._top = new System.Windows.Forms.Panel();
            this.lblKid = new System.Windows.Forms.Label();
            this._kidBox = new System.Windows.Forms.ComboBox();
            this.lblTemplate = new System.Windows.Forms.Label();
            this._templateBox = new System.Windows.Forms.ComboBox();
            this._startBtn = new System.Windows.Forms.Button();
            this._endBtn = new System.Windows.Forms.Button();
            this._prefillHost = new System.Windows.Forms.Panel();
            this._prefillTable = new System.Windows.Forms.TableLayoutPanel();
            this._status = new System.Windows.Forms.StatusStrip();
            this._hubLbl = new System.Windows.Forms.ToolStripStatusLabel();
            this.toolStripSeparator1 = new System.Windows.Forms.ToolStripStatusLabel();
            this._sessionLbl = new System.Windows.Forms.ToolStripStatusLabel();
            this.toolStripSeparator2 = new System.Windows.Forms.ToolStripStatusLabel();
            this._templateLbl = new System.Windows.Forms.ToolStripStatusLabel();
            this.toolStripSeparator3 = new System.Windows.Forms.ToolStripStatusLabel();
            this._msgLbl = new System.Windows.Forms.ToolStripStatusLabel();
            this._retryTimer = new System.Windows.Forms.Timer(this.components);
            this._top.SuspendLayout();
            this._prefillHost.SuspendLayout();
            this._status.SuspendLayout();
            this.SuspendLayout();
            // 
            // _top
            // 
            this._top.BackColor = System.Drawing.Color.White;
            this._top.Controls.Add(this.lblKid);
            this._top.Controls.Add(this._kidBox);
            this._top.Controls.Add(this.lblTemplate);
            this._top.Controls.Add(this._templateBox);
            this._top.Controls.Add(this._startBtn);
            this._top.Controls.Add(this._endBtn);
            this._top.Dock = System.Windows.Forms.DockStyle.Top;
            this._top.Location = new System.Drawing.Point(0, 0);
            this._top.Name = "_top";
            this._top.Padding = new System.Windows.Forms.Padding(12);
            this._top.Size = new System.Drawing.Size(371, 86);
            this._top.TabIndex = 0;
            // 
            // lblKid
            // 
            this.lblKid.AutoSize = true;
            this.lblKid.Location = new System.Drawing.Point(10, 14);
            this.lblKid.Name = "lblKid";
            this.lblKid.Size = new System.Drawing.Size(65, 13);
            this.lblKid.TabIndex = 0;
            this.lblKid.Text = "Tablet (kid):";
            // 
            // _kidBox
            // 
            this._kidBox.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this._kidBox.Location = new System.Drawing.Point(95, 10);
            this._kidBox.Name = "_kidBox";
            this._kidBox.Size = new System.Drawing.Size(140, 21);
            this._kidBox.TabIndex = 1;
            // 
            // lblTemplate
            // 
            this.lblTemplate.AutoSize = true;
            this.lblTemplate.Location = new System.Drawing.Point(15, 48);
            this.lblTemplate.Name = "lblTemplate";
            this.lblTemplate.Size = new System.Drawing.Size(55, 13);
            this.lblTemplate.TabIndex = 2;
            this.lblTemplate.Text = "Template:";
            // 
            // _templateBox
            // 
            this._templateBox.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this._templateBox.Location = new System.Drawing.Point(95, 44);
            this._templateBox.Name = "_templateBox";
            this._templateBox.Size = new System.Drawing.Size(140, 21);
            this._templateBox.TabIndex = 3;
            // 
            // _startBtn
            // 
            this._startBtn.Location = new System.Drawing.Point(241, 10);
            this._startBtn.Name = "_startBtn";
            this._startBtn.Size = new System.Drawing.Size(100, 21);
            this._startBtn.TabIndex = 4;
            this._startBtn.Text = "Start";
            this._startBtn.UseVisualStyleBackColor = true;
            // 
            // _endBtn
            // 
            this._endBtn.Location = new System.Drawing.Point(241, 44);
            this._endBtn.Name = "_endBtn";
            this._endBtn.Size = new System.Drawing.Size(100, 21);
            this._endBtn.TabIndex = 5;
            this._endBtn.Text = "End";
            this._endBtn.UseVisualStyleBackColor = true;
            this._endBtn.Click += new System.EventHandler(this._endBtn_Click);
            // 
            // _prefillHost
            // 
            this._prefillHost.AutoScroll = true;
            this._prefillHost.BackColor = System.Drawing.Color.White;
            this._prefillHost.Controls.Add(this._prefillTable);
            this._prefillHost.Dock = System.Windows.Forms.DockStyle.Fill;
            this._prefillHost.Location = new System.Drawing.Point(0, 86);
            this._prefillHost.Name = "_prefillHost";
            this._prefillHost.Padding = new System.Windows.Forms.Padding(12, 0, 12, 12);
            this._prefillHost.Size = new System.Drawing.Size(371, 167);
            this._prefillHost.TabIndex = 1;
            // 
            // _prefillTable
            // 
            this._prefillTable.AutoSize = true;
            this._prefillTable.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
            this._prefillTable.ColumnCount = 2;
            this._prefillTable.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle());
            this._prefillTable.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
            this._prefillTable.Dock = System.Windows.Forms.DockStyle.Top;
            this._prefillTable.Location = new System.Drawing.Point(12, 0);
            this._prefillTable.Margin = new System.Windows.Forms.Padding(0);
            this._prefillTable.Name = "_prefillTable";
            this._prefillTable.Padding = new System.Windows.Forms.Padding(6);
            this._prefillTable.RowCount = 1;
            this._prefillTable.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 20F));
            this._prefillTable.Size = new System.Drawing.Size(347, 32);
            this._prefillTable.TabIndex = 0;
            // 
            // _status
            // 
            this._status.BackColor = System.Drawing.Color.White;
            this._status.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this._hubLbl,
            this.toolStripSeparator1,
            this._sessionLbl,
            this.toolStripSeparator2,
            this._templateLbl,
            this.toolStripSeparator3,
            this._msgLbl});
            this._status.Location = new System.Drawing.Point(0, 253);
            this._status.Name = "_status";
            this._status.Size = new System.Drawing.Size(371, 22);
            this._status.SizingGrip = false;
            this._status.TabIndex = 2;
            // 
            // _hubLbl
            // 
            this._hubLbl.ForeColor = System.Drawing.Color.Firebrick;
            this._hubLbl.Name = "_hubLbl";
            this._hubLbl.Size = new System.Drawing.Size(70, 17);
            this._hubLbl.Text = "Hub: offline";
            // 
            // toolStripSeparator1
            // 
            this.toolStripSeparator1.Name = "toolStripSeparator1";
            this.toolStripSeparator1.Size = new System.Drawing.Size(10, 17);
            this.toolStripSeparator1.Text = "|";
            // 
            // _sessionLbl
            // 
            this._sessionLbl.Name = "_sessionLbl";
            this._sessionLbl.Size = new System.Drawing.Size(64, 17);
            this._sessionLbl.Text = "Session: —";
            // 
            // toolStripSeparator2
            // 
            this.toolStripSeparator2.Name = "toolStripSeparator2";
            this.toolStripSeparator2.Size = new System.Drawing.Size(10, 17);
            this.toolStripSeparator2.Text = "|";
            // 
            // _templateLbl
            // 
            this._templateLbl.Name = "_templateLbl";
            this._templateLbl.Size = new System.Drawing.Size(74, 17);
            this._templateLbl.Text = "Template: —";
            // 
            // toolStripSeparator3
            // 
            this.toolStripSeparator3.Name = "toolStripSeparator3";
            this.toolStripSeparator3.Size = new System.Drawing.Size(10, 17);
            this.toolStripSeparator3.Text = "|";
            // 
            // _msgLbl
            // 
            this._msgLbl.Name = "_msgLbl";
            this._msgLbl.Size = new System.Drawing.Size(0, 17);
            // 
            // _retryTimer
            // 
            this._retryTimer.Interval = 3000;
            this._retryTimer.Tick += new System.EventHandler(this._retryTimer_Tick);
            // 
            // ReceptionForm
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(371, 275);
            this.Controls.Add(this._prefillHost);
            this.Controls.Add(this._status);
            this.Controls.Add(this._top);
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedToolWindow;
            this.Icon = ((System.Drawing.Icon)(resources.GetObject("$this.Icon")));
            this.MaximizeBox = false;
            this.Name = "ReceptionForm";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;
            this.Text = "GuestGate — Reception";
            this._top.ResumeLayout(false);
            this._top.PerformLayout();
            this._prefillHost.ResumeLayout(false);
            this._prefillHost.PerformLayout();
            this._status.ResumeLayout(false);
            this._status.PerformLayout();
            this.ResumeLayout(false);
            this.PerformLayout();

        }
        #endregion
    }
}
