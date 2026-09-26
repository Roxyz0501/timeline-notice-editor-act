using System;
using System.Collections.Generic;
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
        private readonly Button folder = B("Folder"), open = B("OpenXml"), images = B("ImageFolder"), save = B("SaveXml"), reset = B("ResetNotice"), reload = B("Reload"), remote = B("LoadRemote");
        private readonly ComboBox files = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 300 };
        private readonly TextBox filter = new TextBox { Width = 190 };
        private readonly CheckedListBox notices = new CheckedListBox { Dock = DockStyle.Fill, HorizontalScrollbar = true, IntegralHeight = false, CheckOnClick = false };
        private readonly Label path = L(), message = L(), detail = L(), scope = L(), imagePath = L();
        private readonly NumericUpDown left = N(-100000, 100000, 1), top = N(-100000, 100000, 1), scale = N(.01m, 20, .05m);
        private readonly CheckBox preview = new CheckBox { AutoSize = true, Tag = "DesktopPreview" }, locked = new CheckBox { AutoSize = true, Tag = "LockPreview" };
        private readonly CheckBox detailView = new CheckBox { AutoSize = true, Tag = "DetailView" };
        private readonly PreviewCanvas canvas = new PreviewCanvas();
        private sealed class PreviewState
        {
            internal Notice Notice;
            internal BitmapSource Image, Rendered;
            internal string Path = "";
            internal bool Enabled, Loaded;
            internal CancellationTokenSource Cancellation;
            internal PreviewWindow Window;
        }
        private readonly Dictionary<int, PreviewState> previews = new Dictionary<int, PreviewState>();
        private TimelineDocument document;

        private Notice selected;
        private BitmapSource rendered;
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
            layout.Controls.Add(Flow(save, reload), 0, 10);
            message.AutoSize = false; message.Dock = DockStyle.Bottom; message.Height = 75; message.Padding = new Padding(10); message.BackColor = Color.FromArgb(244, 247, 251);
            scroll.Controls.Add(layout); Controls.Add(scroll); Controls.Add(message);
            folder.Click += (s, e) => ChooseFolder(false); images.Click += (s, e) => ChooseFolder(true);
            open.Click += (s, e) => { using (var dialog = new OpenFileDialog { Filter = "XML|*.xml", InitialDirectory = settings.TimelineFolder, RestoreDirectory = true }) if (dialog.ShowDialog(this) == DialogResult.OK) Open(dialog.FileName); };
            files.SelectedIndexChanged += (s, e) => { if (!populating && files.SelectedItem != null) { var requested = Path.Combine(settings.TimelineFolder, (string)files.SelectedItem); if (!Open(requested)) { populating = true; files.SelectedItem = document == null ? null : Path.GetFileName(document.File.PathName); populating = false; } } };
            filter.TextChanged += (s, e) => PopulateNotices(); notices.SelectedIndexChanged += (s, e) => SelectNotice();
            notices.ItemCheck += (s, e) => { if (!populating) SetPreview((Notice)notices.Items[e.Index], e.NewValue == CheckState.Checked, false); };
            left.ValueChanged += ChangePosition; top.ValueChanged += ChangePosition; scale.ValueChanged += ChangeScale;
            preview.CheckedChanged += (s, e) => { if (!populating && selected != null) SetPreview(selected, preview.Checked, true); };
            locked.CheckedChanged += (s, e) => { foreach (var state in previews.Values) UpdatePreview(state); };
            detailView.CheckedChanged += (s, e) => { canvas.DetailView = detailView.Checked; canvas.Invalidate(); };
            canvas.Moved += MoveNotice;
            reset.Click += (s, e) => { if (selected == null) return; selected.Changes.Clear(); FillValues(); Render(); UpdateButtons(); notices.Invalidate(); };
            save.Click += (s, e) => Save();
            reload.Click += (s, e) => { if (document != null) Open(document.File.PathName); };
            remote.Click += (s, e) => { if (selected != null) LoadImage(selected, true); };
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
                if (image) { settings.ExtraImageFolder = dialog.SelectedPath; foreach (var state in previews.Values.ToArray()) { state.Loaded = false; if (state.Enabled || state.Notice == selected) LoadImage(state.Notice, false); } }
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
        internal bool ConfirmDiscard() => (document == null || !document.Dirty) || MessageBox.Show(this, T("Discard"), "Timeline Notice Editor", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes;
        internal bool Open(string filename)
        {
            if (!ConfirmDiscard()) return false;
            try
            {
                var next = new TimelineDocument(filename, settings.BackupDirectory);
                Clear(); document = next;
                settings.TimelineFolder = Path.GetDirectoryName(filename); PopulateFiles();
                populating = true; files.SelectedItem = Path.GetFileName(filename); populating = false;
                path.Text = filename; SettingsChanged?.Invoke(this, EventArgs.Empty);
                PopulateNotices(); Status("Loaded", document.Notices.Count); return true;
            }
            catch (Exception ex) { Error(ex); return false; }
        }
        private void Clear()
        {
            foreach (var state in previews.Values) { state.Cancellation?.Cancel(); state.Cancellation?.Dispose(); state.Window?.Close(); }
            previews.Clear(); selected = null; document = null; rendered = null; canvas.SetImage(null); notices.Items.Clear(); UpdateButtons();
        }
        private void PopulateNotices()
        {
            var previous = selected; populating = true; notices.Items.Clear();
            if (document != null) notices.Items.AddRange(document.Notices.Where(n => n.ToString().IndexOf(filter.Text, StringComparison.CurrentCultureIgnoreCase) >= 0).Cast<object>().ToArray());
            for (int i = 0; i < notices.Items.Count; i++) { PreviewState state; if (previews.TryGetValue(((Notice)notices.Items[i]).Id, out state)) notices.SetItemChecked(i, state.Enabled); }
            if (previous != null && notices.Items.Contains(previous)) notices.SelectedItem = previous; else if (notices.Items.Count > 0) notices.SelectedIndex = 0;
            populating = false; SelectNotice();
        }
        private void SelectNotice()
        {
            if (populating) return; selected = notices.SelectedItem as Notice;
            rendered = null; canvas.SetImage(null); FillValues(); UpdateLabels(); UpdateButtons();
            populating = true; preview.Checked = selected != null && State(selected).Enabled; populating = false;
            if (selected != null) LoadImage(selected, false);
        }
        private void FillValues()
        {
            populating = true;
            if (selected != null)
            {
                left.Value = Bound(left, Notice.Number(selected.Get("left"), document.Default(selected, "left", -1)));
                top.Value = Bound(top, Notice.Number(selected.Get("top"), document.Default(selected, "top", -1)));
                scale.Value = Bound(scale, Notice.Number(selected.Get("scale"), document.Default(selected, "scale", 1)));
            }
            populating = false;
        }
        private void UpdateLabels()
        {
            detail.Text = selected == null ? T("SelectNotice") : "L" + selected.Line + "  " + selected.Section + "  " + selected.Time + "  " + selected.Context;
            scope.Text = selected == null ? T("EditorHint") : T("ImageScope");
        }
        private void UpdateButtons()
        {
            save.Enabled = document?.Dirty == true;
            left.Enabled = top.Enabled = selected != null;
            scale.Enabled = selected?.IsImage == true; reset.Enabled = selected != null; reload.Enabled = document != null;
            preview.Enabled = selected != null;
        }
        private void ChangePosition(object sender, EventArgs e)
        {
            if (populating || selected == null) return;
            selected.Set("left", (double)left.Value); selected.Set("top", (double)top.Value);

            UpdatePosition(); UpdateButtons(); notices.Invalidate();
        }
        private void ChangeScale(object sender, EventArgs e) { if (populating || selected?.IsImage != true) return; selected.Set("scale", (double)scale.Value); Render(); UpdateButtons(); notices.Invalidate(); }
        private void MoveNotice(double x, double y)
        {
            if (selected == null) return;
            populating = true; left.Value = Bound(left, x); top.Value = Bound(top, y); populating = false; ChangePosition(null, EventArgs.Empty);
        }
        private PreviewState State(Notice notice)
        {
            PreviewState state;
            if (!previews.TryGetValue(notice.Id, out state)) previews.Add(notice.Id, state = new PreviewState { Notice = notice });
            return state;
        }
        private void SetPreview(Notice notice, bool enabled, bool updateList)
        {
            var state = State(notice); state.Enabled = enabled;
            populating = true;
            if (selected == notice) preview.Checked = enabled;
            int index = notices.Items.IndexOf(notice);
            if (updateList && index >= 0) notices.SetItemChecked(index, enabled);
            populating = false;
            if (enabled && !state.Loaded) LoadImage(notice, false); else UpdatePreview(state);
        }
        private async void LoadImage(Notice notice, bool allowRemote)
        {
            if (document == null) return;
            var state = State(notice);
            if (state.Loaded && !allowRemote) { RenderState(state); return; }
            state.Cancellation?.Cancel(); state.Cancellation?.Dispose(); state.Cancellation = new CancellationTokenSource();
            var token = state.Cancellation.Token;
            var reference = notice.Get("image");
            if (selected == notice) imagePath.Text = T("LoadingImage");
            try
            {
                var result = await new ImageResolver(document.File.PathName, settings.ExtraImageFolder).LoadAsync(reference, false, allowRemote, token).ConfigureAwait(false);
                OnUi(() => {
                    if (token.IsCancellationRequested) return;
                    state.Image = result.Item1; state.Path = string.IsNullOrEmpty(reference) ? T("NoImage") : result.Item1 == null ? T("MissingImage", reference) : result.Item2;
                    state.Loaded = true; RenderState(state);
                });
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                OnUi(() => {
                    if (token.IsCancellationRequested) return;
                    state.Image = null; var translated = T(ex.Message); state.Path = (translated == ex.Message ? T("FileProblem") : translated) + "  " + reference;
                    state.Loaded = true; RenderState(state);
                });
            }
        }
        private void OnUi(Action action)
        {
            if (IsDisposed || !IsHandleCreated) return;
            try { BeginInvoke(new Action(() => { if (!IsDisposed) action(); })); }
            catch (InvalidOperationException) { }
        }
        private void Render() { if (selected != null) RenderState(State(selected)); }
        private void RenderState(PreviewState state)
        {
            try
            {
                double size = Notice.Number(state.Notice.Get("scale"), document.Default(state.Notice, "scale", 1));
                state.Rendered = NoticePreview.Render(state.Image, size, T("MissingImage", state.Notice.Get("image")));
                if (state.Notice == selected)
                {
                    rendered = state.Rendered; imagePath.Text = state.Path; canvas.SetImage(rendered); UpdatePosition();
                }
                else UpdatePreview(state);
            }
            catch (Exception ex) { state.Rendered = null; if (state.Notice == selected) { rendered = null; canvas.SetImage(null); } UpdatePreview(state); Error(ex); }
        }
        private PointF Position(PreviewState state)
        {
            double x = Notice.Number(state.Notice.Get("left"), document.Default(state.Notice, "left", -1));
            double y = Notice.Number(state.Notice.Get("top"), document.Default(state.Notice, "top", -1));
            if (x == -1 && y == -1 && state.Rendered != null)
            {
                var area = System.Windows.SystemParameters.WorkArea; x = area.Left + (area.Width - state.Rendered.Width) / 2; y = area.Top + (area.Height - state.Rendered.Height) / 2;
            }
            return new PointF((float)x, (float)y);
        }
        private void UpdatePosition()
        {
            if (selected == null) return;
            var state = State(selected); var position = Position(state);
            canvas.X = position.X; canvas.Y = position.Y; canvas.Invalidate(); UpdatePreview(state);
        }
        private void UpdatePreview(PreviewState state)
        {
            if (state.Enabled && state.Rendered != null && state.Window == null)
            {
                state.Window = new PreviewWindow();
                state.Window.Moved += (x, y) => MovePreview(state, x, y);
            }
            var position = Position(state);
            state.Window?.Present(state.Rendered, position.X, position.Y, state.Enabled, locked.Checked);
        }
        private void MovePreview(PreviewState state, double x, double y)
        {
            state.Notice.Set("left", Math.Max(-100000, Math.Min(100000, x)));
            state.Notice.Set("top", Math.Max(-100000, Math.Min(100000, y)));
            if (state.Notice == selected) { FillValues(); UpdatePosition(); }
            else UpdatePreview(state);
            UpdateButtons(); notices.Invalidate();
        }
        private void Save()
        {
            try
            {
                if (document == null || !document.Dirty) return;
                int index = selected?.Id ?? 1;
                var backup = document.Save(settings.BackupDirectoryResolver == null ? settings.BackupDirectory : settings.BackupDirectoryResolver());
                document = new TimelineDocument(document.File.PathName, settings.BackupDirectory);
                foreach (var state in previews.Values) state.Notice = document.Notices.Single(n => n.Id == state.Notice.Id);
                selected = document.Notices.FirstOrDefault(n => n.Id == index);
                PopulateNotices(); Status("SavedXml", backup); UpdateButtons();
            }
            catch (Exception ex) { Error(ex); }
        }
        private void Error(Exception ex) { var key = ex.GetBaseException().Message; Status("OperationFailed", T(key) == key ? T("FileProblem") + " (" + ex.GetBaseException().GetType().Name + ")" : T(key)); }
        private static Label L() => new Label { AutoSize = false, Width = 60, Height = 32, ForeColor = Color.FromArgb(48, 61, 80), TextAlign = ContentAlignment.MiddleLeft };
        private static Button B(string key) => new Button { Tag = key, AutoSize = true, MinimumSize = new Size(100, 32), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(244, 247, 251), ForeColor = Color.FromArgb(48, 61, 80), Margin = new Padding(3) };
        private static NumericUpDown N(decimal min, decimal max, decimal increment) => new NumericUpDown { Minimum = min, Maximum = max, Increment = increment, DecimalPlaces = 2, Width = 105 };
        private static decimal Bound(NumericUpDown n, double value) => (decimal)Math.Max((double)n.Minimum, Math.Min((double)n.Maximum, value));
        private static Control Flow(params Control[] controls) { var p = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, AutoScroll = true }; p.Controls.AddRange(controls); return p; }
        protected override void Dispose(bool disposing) { if (disposing) { foreach (var state in previews.Values) { state.Cancellation?.Cancel(); state.Cancellation?.Dispose(); state.Window?.Close(); } } base.Dispose(disposing); }
    }
}
