using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using System.Windows.Media.Imaging;

namespace TimelineNoticeEditor
{
    internal sealed class EditorControl : UserControl
    {
        private readonly PluginSettings settings;
        private readonly Button folder = B("Folder"), open = B("OpenXml"), images = B("ImageFolder"), save = B("SaveXml"), saveGlobal = B("SaveGlobal"), reset = B("ResetNotice"), reload = B("Reload"), remote = B("LoadRemote");
        private readonly ComboBox files = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 300 };
        private readonly TextBox filter = new TextBox { Width = 190 };
        private readonly ListBox notices = new ListBox { Dock = DockStyle.Fill, HorizontalScrollbar = true, IntegralHeight = false };
        private readonly Label path = L(), message = L(), detail = L(), scope = L(), imagePath = L();
        private readonly NumericUpDown left = N(-100000, 100000, 1), top = N(-100000, 100000, 1), scale = N(.01m, 20, .05m);
        private readonly CheckBox preview = new CheckBox { AutoSize = true, Tag = "DesktopPreview" }, locked = new CheckBox { AutoSize = true, Tag = "LockPreview" };
        private readonly CheckBox detailView = new CheckBox { AutoSize = true, Tag = "DetailView" };
        private readonly PreviewCanvas canvas = new PreviewCanvas();
        private PreviewWindow overlay;
        private TimelineDocument document;
        private VisualSettings visual;
        private Notice selected;
        private BitmapSource loadedImage, rendered;
        private string loadedReference = "";
        private CancellationTokenSource imageCancellation;
        private bool populating;
        private string messageKey = "EditorHint";
        private object[] messageArgs = new object[0];
        internal event EventHandler SettingsChanged;
        internal EditorControl(PluginSettings settings)
        {
            this.settings = settings; Dock = DockStyle.Fill; BackColor = Color.White;
            var scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
            var layout = new TableLayoutPanel { Dock = DockStyle.Top, Height = 960, MinimumSize = new Size(680, 960), ColumnCount = 1, Padding = new Padding(12) };
            foreach (var h in new[] { 40, 40, 40, 160, 42, 58, 42, 48, 290, 52, 58 }) layout.RowStyles.Add(new RowStyle(SizeType.Absolute, h));
            layout.Controls.Add(Flow(folder, open, images), 0, 0);
            path.Dock = DockStyle.Fill; layout.Controls.Add(path, 0, 1);
            var filterLabel = L(); filterLabel.Tag = "Filter";
            layout.Controls.Add(Flow(files, filterLabel, filter), 0, 2); layout.Controls.Add(notices, 0, 3);
            detail.Dock = DockStyle.Fill; layout.Controls.Add(detail, 0, 4);
            scope.Dock = DockStyle.Fill; scope.ForeColor = Color.FromArgb(137, 83, 18); layout.Controls.Add(scope, 0, 5);
            var xLabel = L(); xLabel.Text = "X"; var yLabel = L(); yLabel.Text = "Y"; var scaleLabel = L(); scaleLabel.Tag = "Scale";
            layout.Controls.Add(Flow(xLabel, left, yLabel, top, scaleLabel, scale, reset), 0, 6);
            layout.Controls.Add(Flow(preview, locked, detailView, remote), 0, 7); layout.Controls.Add(canvas, 0, 8);
            imagePath.Dock = DockStyle.Fill; layout.Controls.Add(imagePath, 0, 9);
            layout.Controls.Add(Flow(save, saveGlobal, reload), 0, 10);
            message.AutoSize = false; message.Dock = DockStyle.Bottom; message.Height = 75; message.Padding = new Padding(10); message.BackColor = Color.FromArgb(244, 247, 251);
            scroll.Controls.Add(layout); Controls.Add(scroll); Controls.Add(message);
            folder.Click += (s, e) => ChooseFolder(false); images.Click += (s, e) => ChooseFolder(true);
            open.Click += (s, e) => { using (var dialog = new OpenFileDialog { Filter = "XML|*.xml", InitialDirectory = settings.TimelineFolder, RestoreDirectory = true }) if (dialog.ShowDialog(this) == DialogResult.OK) Open(dialog.FileName); };
            files.SelectedIndexChanged += (s, e) => { if (!populating && files.SelectedItem != null) { var requested = Path.Combine(settings.TimelineFolder, (string)files.SelectedItem); if (!Open(requested)) { populating = true; files.SelectedItem = document == null ? null : Path.GetFileName(document.File.PathName); populating = false; } } };
            filter.TextChanged += (s, e) => PopulateNotices(); notices.SelectedIndexChanged += (s, e) => SelectNotice();
            left.ValueChanged += ChangePosition; top.ValueChanged += ChangePosition; scale.ValueChanged += ChangeScale;
            preview.CheckedChanged += (s, e) => UpdateOverlay(); locked.CheckedChanged += (s, e) => UpdateOverlay();
            detailView.CheckedChanged += (s, e) => { canvas.DetailView = detailView.Checked; canvas.Invalidate(); };
            canvas.Moved += MoveNotice;
            reset.Click += (s, e) => { if (selected == null) return; if (selected.IsImage) selected.Changes.Clear(); else if (visual != null) { visual.Left = visual.OriginalLeft; visual.Top = visual.OriginalTop; } FillValues(); Render(); UpdateButtons(); notices.Invalidate(); };
            save.Click += (s, e) => Save(false); saveGlobal.Click += (s, e) => Save(true);
            reload.Click += (s, e) => { if (document != null) Open(document.File.PathName); };
            remote.Click += (s, e) => LoadImage(true);
            if (!Directory.Exists(settings.TimelineFolder)) settings.TimelineFolder = PluginSettings.DiscoverFolder();
            PopulateFiles(); ApplyLanguage(); UpdateButtons();
        }
        private string T(string key, params object[] args) => Localization.Get(settings.Language, key, args);
        private void Status(string key, params object[] args) { messageKey = key; messageArgs = args; message.Text = T(key, args); }
        internal void ApplyLanguage()
        {
            Translate(this); canvas.Hint = T("DragHint"); message.Text = T(messageKey, messageArgs); UpdateLabels(); Render();
        }
        private void Translate(Control root) { if (root.Tag is string) root.Text = T((string)root.Tag); foreach (Control child in root.Controls) Translate(child); }
        private void ChooseFolder(bool image)
        {
            using (var dialog = new FolderBrowserDialog { SelectedPath = image ? settings.ExtraImageFolder : settings.TimelineFolder, Description = T(image ? "ImageFolder" : "Folder") })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                if (image) { settings.ExtraImageFolder = dialog.SelectedPath; LoadImage(false); }
                else { if (!ConfirmDiscard()) return; Clear(); settings.TimelineFolder = dialog.SelectedPath; PopulateFiles(); }
                SettingsChanged?.Invoke(this, EventArgs.Empty);
            }
        }
        private void PopulateFiles()
        {
            populating = true; files.Items.Clear();
            try { if (Directory.Exists(settings.TimelineFolder)) files.Items.AddRange(Directory.GetFiles(settings.TimelineFolder, "*.xml").Select(Path.GetFileName).OrderBy(x => x).Cast<object>().ToArray()); }
            catch (Exception ex) { Error(ex); }
            finally { populating = false; path.Text = settings.TimelineFolder; }
        }
        internal bool ConfirmDiscard() => (document == null || !document.Dirty) && (visual == null || !visual.Dirty) || MessageBox.Show(this, T("Discard"), "Timeline Notice Editor", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes;
        internal bool Open(string filename)
        {
            if (!ConfirmDiscard()) return false;
            try
            {
                var next = new TimelineDocument(filename);
                VisualSettings nextVisual = null; var configPath = Path.Combine(Path.GetDirectoryName(filename), "Timeline.config");
                string warning = null;
                if (File.Exists(configPath)) { try { nextVisual = new VisualSettings(configPath); } catch { warning = "ConfigUnavailable"; } } else warning = "ConfigUnavailable";
                Clear(); document = next; visual = nextVisual;
                settings.TimelineFolder = Path.GetDirectoryName(filename); PopulateFiles();
                populating = true; files.SelectedItem = Path.GetFileName(filename); populating = false;
                path.Text = filename; SettingsChanged?.Invoke(this, EventArgs.Empty);
                PopulateNotices(); Status(warning ?? "Loaded", document.Notices.Count); return true;
            }
            catch (Exception ex) { Error(ex); return false; }
        }
        private void Clear()
        {
            imageCancellation?.Cancel(); selected = null; document = null; visual = null; loadedImage = rendered = null; canvas.SetImage(null); notices.Items.Clear(); UpdateOverlay(); UpdateButtons();
        }
        private void PopulateNotices()
        {
            var previous = selected; populating = true; notices.Items.Clear();
            if (document != null) notices.Items.AddRange(document.Notices.Where(n => n.ToString().IndexOf(filter.Text, StringComparison.CurrentCultureIgnoreCase) >= 0).Cast<object>().ToArray());
            if (previous != null && notices.Items.Contains(previous)) notices.SelectedItem = previous; else if (notices.Items.Count > 0) notices.SelectedIndex = 0;
            populating = false; SelectNotice();
        }
        private void SelectNotice()
        {
            if (populating) return; selected = notices.SelectedItem as Notice;
            loadedImage = rendered = null; canvas.SetImage(null); UpdateOverlay(); FillValues(); UpdateLabels(); UpdateButtons(); LoadImage(false);
        }
        private void FillValues()
        {
            populating = true;
            if (selected != null)
            {
                left.Value = Bound(left, selected.IsImage ? Notice.Number(selected.Get("left"), document.Default(selected, "left", -1)) : visual?.Left ?? 0);
                top.Value = Bound(top, selected.IsImage ? Notice.Number(selected.Get("top"), document.Default(selected, "top", -1)) : visual?.Top ?? 0);
                scale.Value = Bound(scale, selected.IsImage ? Notice.Number(selected.Get("scale"), document.Default(selected, "scale", 1)) : 1);
            }
            populating = false;
        }
        private void UpdateLabels()
        {
            detail.Text = selected == null ? T("SelectNotice") : "L" + selected.Line + "  " + selected.Section + "  " + selected.Time + "  " + selected.Context;
            scope.Text = selected == null ? T("EditorHint") : T(selected.IsImage ? "ImageScope" : "GlobalScope");
        }
        private void UpdateButtons()
        {
            save.Enabled = document?.Dirty == true; saveGlobal.Enabled = visual?.Dirty == true;
            left.Enabled = top.Enabled = selected != null && (selected.IsImage || visual != null);
            scale.Enabled = selected?.IsImage == true; reset.Enabled = selected != null; reload.Enabled = document != null;
        }
        private void ChangePosition(object sender, EventArgs e)
        {
            if (populating || selected == null) return;
            if (selected.IsImage) { selected.Set("left", (double)left.Value); selected.Set("top", (double)top.Value); }
            else if (visual != null) { visual.Left = (double)left.Value; visual.Top = (double)top.Value; }
            UpdatePosition(); UpdateButtons(); notices.Invalidate();
        }
        private void ChangeScale(object sender, EventArgs e) { if (populating || selected?.IsImage != true) return; selected.Set("scale", (double)scale.Value); Render(); UpdateButtons(); notices.Invalidate(); }
        private void MoveNotice(double x, double y)
        {
            if (selected == null || (!selected.IsImage && visual == null)) return;
            populating = true; left.Value = Bound(left, x); top.Value = Bound(top, y); populating = false; ChangePosition(null, EventArgs.Empty);
        }
        private async void LoadImage(bool allowRemote)
        {
            imageCancellation?.Cancel(); imageCancellation?.Dispose(); imageCancellation = new CancellationTokenSource(); var token = imageCancellation.Token;
            var notice = selected; if (notice == null || document == null) return;
            var reference = notice.Get(notice.IsImage ? "image" : "icon");
            if (!notice.IsImage && reference.Length == 0) reference = (string)visual?.Style(notice)?.Element("Icon") ?? "";
            loadedReference = reference; imagePath.Text = T("LoadingImage");
            try
            {
                var result = await new ImageResolver(document.File.PathName, settings.ExtraImageFolder).LoadAsync(reference, !notice.IsImage, allowRemote, token);
                if (IsDisposed || token.IsCancellationRequested || selected != notice) return;
                loadedImage = result.Item1; imagePath.Text = string.IsNullOrEmpty(reference) ? T("NoImage") : result.Item1 == null ? T("MissingImage", reference) : result.Item2;
                Render();
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { if (!IsDisposed && !token.IsCancellationRequested && selected == notice) { loadedImage = null; var translated = T(ex.Message); imagePath.Text = (translated == ex.Message ? T("FileProblem") : translated) + "  " + reference; Render(); } }
        }
        private void Render()
        {
            if (selected == null) return;
            try { rendered = NoticePreview.Render(selected, loadedImage, visual, (double)scale.Value, T("MissingImage", loadedReference)); canvas.SetImage(rendered); UpdatePosition(); }
            catch (Exception ex) { rendered = null; canvas.SetImage(null); UpdateOverlay(); Error(ex); }
        }
        private void UpdatePosition()
        {
            double x = (double)left.Value, y = (double)top.Value;
            if (selected?.IsImage == true && x == -1 && y == -1 && rendered != null)
            {
                var area = System.Windows.SystemParameters.WorkArea; x = area.Left + (area.Width - rendered.Width) / 2; y = area.Top + (area.Height - rendered.Height) / 2;
            }
            canvas.X = x; canvas.Y = y; canvas.Invalidate(); UpdateOverlay();
        }
        private void UpdateOverlay()
        {
            if (preview.Checked && rendered != null && overlay == null) { overlay = new PreviewWindow(); overlay.Moved += MoveNotice; }
            overlay?.Present(rendered, canvas.X, canvas.Y, preview.Checked, locked.Checked);
        }
        private void Save(bool global)
        {
            try
            {
                if (global)
                {
                    if (visual == null || !visual.Dirty) return;
                    var backup = visual.Save(); visual = new VisualSettings(visual.File.PathName); Status("SavedGlobal", backup);
                }
                else
                {
                    if (document == null || !document.Dirty) return;
                    int index = selected?.Id ?? 1; var backup = document.Save(); document = new TimelineDocument(document.File.PathName); selected = document.Notices.FirstOrDefault(n => n.Id == index); PopulateNotices(); Status("SavedXml", backup);
                }
                UpdateButtons();
            }
            catch (Exception ex) { Error(ex); }
        }
        private void Error(Exception ex) { var key = ex.GetBaseException().Message; Status("OperationFailed", T(key) == key ? T("FileProblem") + " (" + ex.GetBaseException().GetType().Name + ")" : T(key)); }
        private static Label L() => new Label { AutoSize = false, Width = 60, Height = 32, ForeColor = Color.FromArgb(48, 61, 80), TextAlign = ContentAlignment.MiddleLeft };
        private static Button B(string key) => new Button { Tag = key, AutoSize = true, MinimumSize = new Size(100, 32), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(244, 247, 251), ForeColor = Color.FromArgb(48, 61, 80), Margin = new Padding(3) };
        private static NumericUpDown N(decimal min, decimal max, decimal increment) => new NumericUpDown { Minimum = min, Maximum = max, Increment = increment, DecimalPlaces = 2, Width = 105 };
        private static decimal Bound(NumericUpDown n, double value) => (decimal)Math.Max((double)n.Minimum, Math.Min((double)n.Maximum, value));
        private static Control Flow(params Control[] controls) { var p = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, AutoScroll = true }; p.Controls.AddRange(controls); return p; }
        protected override void Dispose(bool disposing) { if (disposing) { imageCancellation?.Cancel(); imageCancellation?.Dispose(); overlay?.Close(); } base.Dispose(disposing); }
    }
}
