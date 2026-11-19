using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using TimelineNoticeEditor;

internal static partial class Program
{
    private static void PreviewResources()
    {
        var budget = new PreviewBudget(100);
        var kept = budget.Reserve(60);
        Check(budget.Reserve(41) == null && budget.Used == 60, "budget denial preserves active reservation");
        var temporary = budget.Reserve(40);
        Check(budget.Peak == 100, "temporary and retained buffers share one budget");
        temporary.Dispose(); kept.Shrink(30); kept.Dispose(); kept.Dispose();
        Check(budget.Used == 0, "budget shrink and repeated release balance");

        var root = Path.Combine(artifacts, "preview-budget"); Directory.CreateDirectory(root);
        var pixels = new byte[64 * 64 * 4];
        for (var i = 0; i < pixels.Length; i += 4) { pixels[i] = 80; pixels[i + 1] = 160; pixels[i + 3] = 255; }
        var source = BitmapSource.Create(64, 64, 96, 96, PixelFormats.Bgra32, null, pixels, 256); source.Freeze();
        using (var canvas = new PreviewCanvas())
        {
            var rendered = NoticePreview.Render(source, 1, ""); canvas.SetImage(rendered); canvas.SetImage(rendered);
            var bitmap = (System.Drawing.Bitmap)typeof(PreviewCanvas).GetField("bitmap", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(canvas);
            Check(bitmap.GetPixel(5, 5).B == 80 && bitmap.GetPixel(5, 5).G == 160 && bitmap.GetPixel(5, 5).A == 255, "direct pixel copy preserves channels and alpha");
            Check(canvas.CopyCount == 1, "identical canvas image skips pixel copy");
        }
        var png = Path.Combine(root, "small.png");
        using (var stream = File.Create(png)) { var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(source)); encoder.Save(stream); }
        var xml = "<timeline>" + string.Concat(Enumerable.Range(0, 24).Select(i => "<i-notice image='small.png' left='0' top='0'/>")) + "</timeline>";
        var file = Path.Combine(root, "small.xml"); File.WriteAllText(file, xml);
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        using (var editor = new EditorControl(new PluginSettings { Language = "en", TimelineFolder = root }, 256 * 1024))
        {
            Check(editor.Open(file), "synthetic timeline opens");
            var list = (CheckedListBox)typeof(EditorControl).GetField("notices", flags).GetValue(editor);
            var states = (IDictionary)typeof(EditorControl).GetField("previews", flags).GetValue(editor);
            long peakResident = 0;
            for (var i = 0; i < 24; i++)
            {
                list.SelectedIndex = i; DrainPreviews(editor);
                peakResident = Math.Max(peakResident, editor.ResidentPreviewBytes);
                Check(editor.PreviewBytes <= 256 * 1024 && states.Count <= 5, "selection cache respects total budget and hidden entry limit");
            }
            Check(editor.LoadCount == 24, "each synthetic selection loads once");
            int renders = editor.RenderCount, copies = editor.CanvasCopies;
            for (var i = 0; i < 30; i++) typeof(EditorControl).GetMethod("Render", flags).Invoke(editor, null);
            Check(editor.RenderCount == renders && editor.CanvasCopies == copies, "unchanged render requests reuse raster and GDI copy");
            var scale = (NumericUpDown)typeof(EditorControl).GetField("scale", flags).GetValue(editor);
            for (var i = 1; i <= 20; i++) scale.Value = 1 + i / 100m;
            Check(editor.RenderCount == renders, "scale burst defers drawing while updating values");
            DrainPreviews(editor);
            Check(editor.RenderCount == renders + 1 && editor.CanvasCopies == copies + 1, "twenty scale changes produce one final render and copy");
            var doc = (TimelineDocument)typeof(EditorControl).GetField("document", flags).GetValue(editor);
            Check(doc.Notices[23].Get("scale") == "1.2", "latest scale is retained in XML edit model");
            renders = editor.RenderCount;
            var x = (NumericUpDown)typeof(EditorControl).GetField("left", flags).GetValue(editor);
            for (var i = 1; i <= 20; i++) x.Value = i;
            Check(editor.RenderCount == renders && File.ReadAllText(file) == xml, "position edits do not render or write XML");
            Console.WriteLine("Preview measurement: 24 x 64x64 sources; peak retained raster bytes=" + peakResident + "; budget=262144; same-state renders=0/30; scale renders=1/20; position renders/writes=0/20.");
            editor.Dispose();
            Check(editor.PreviewBytes == 0 && !editor.WorkPending, "dispose releases all raster reservations and work");
        }
        using (var editor = new EditorControl(new PluginSettings { Language = "en", TimelineFolder = root }, 1024))
        {
            editor.Open(file); DrainPreviews(editor);
            Check(editor.RenderCount == 0 && editor.PreviewBytes == 0, "oversized source denied before raster admission");
        }
        using (var editor = new EditorControl(new PluginSettings { Language = "en", TimelineFolder = root }))
        {
            editor.Open(file);
            var list = (CheckedListBox)typeof(EditorControl).GetField("notices", flags).GetValue(editor);
            for (var i = 1; i < 24; i++) list.SelectedIndex = i;
            DrainPreviews(editor);
            Check(editor.LoadCount <= 2, "rapid selection permits one active decode and only the final queued selection");
        }
    }

    private static void PreviewActiveBudget()
    {
        var root = Path.Combine(artifacts, "preview-budget");
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        using (var editor = new EditorControl(new PluginSettings { Language = "en", TimelineFolder = root }, 90 * 1024))
        {
            editor.Open(Path.Combine(root, "small.xml")); DrainPreviews(editor);
            var list = (CheckedListBox)typeof(EditorControl).GetField("notices", flags).GetValue(editor);
            var states = (IDictionary)typeof(EditorControl).GetField("previews", flags).GetValue(editor);
            list.SetItemChecked(0, true); list.SelectedIndex = 1; list.SetItemChecked(1, true); DrainPreviews(editor);
            var first = StateField<PreviewWindow>(states[1], "Window"); var second = StateField<PreviewWindow>(states[2], "Window");
            Check(first?.Opacity == 1 && second?.Opacity == 1, "two visible previews fit synthetic total budget");
            list.SelectedIndex = 2; list.SetItemChecked(2, true); DrainPreviews(editor);
            Check(StateField<PreviewWindow>(states[3], "Window") == null, "third preview is refused when active rasters fill budget");
            Check(first.Opacity == 1 && second.Opacity == 1 && editor.PreviewBytes <= 90 * 1024, "budget refusal preserves both active previews");
            list.SetItemChecked(0, false); list.SetItemChecked(2, false); list.SetItemChecked(2, true); DrainPreviews(editor);
            Check(StateField<PreviewWindow>(states[3], "Window")?.Opacity == 1 && second.Opacity == 1, "hidden cache is reclaimed to admit requested preview");
            Check(editor.PreviewBytes <= 90 * 1024, "reclaimed allocation stays within total budget");
        }
    }

    private static void DrainPreviews(EditorControl editor)
    {
        var until = DateTime.UtcNow.AddSeconds(3);
        do { editor.ProcessPreviewWork(null, EventArgs.Empty); if (!editor.WorkPending) return; Thread.Sleep(5); } while (DateTime.UtcNow < until);
        throw new Exception("Preview work did not drain.");
    }
}
