using ICSharpCode.SharpZipLib.Zip;
using System.Security.Cryptography;
static string H(Stream s) => Convert.ToHexStringLower(SHA256.HashData(s));
async Task Run(string label, string[] files, bool skipEmpty) {
  var zip = Path.Combine(Path.GetTempPath(), "qa_repro_" + Guid.NewGuid() + ".zip");
  var list = files.Where(f => !skipEmpty || new FileInfo(f).Length > 0).ToList();
  { var fs = new FileStream(zip, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true);
    using var zos = new ZipOutputStream(fs) { IsStreamOwner = true }; zos.SetLevel(6); zos.UseZip64 = UseZip64.Dynamic; zos.Password = "x123";
    foreach (var p in list) { var ext = Path.GetExtension(p).ToLower();
      var e = new ZipEntry(Path.GetFileName(p)) { DateTime = DateTime.UtcNow, CompressionMethod = (ext is ".png" or ".mkv") ? CompressionMethod.Stored : CompressionMethod.Deflated, Size = new FileInfo(p).Length, IsUnicodeText = true, AESKeySize = 256 };
      await zos.PutNextEntryAsync(e); await using (var src = new FileStream(p, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true)) { var buffer = new byte[81920]; int read; while ((read = await src.ReadAsync(buffer)) > 0) { await zos.WriteAsync(buffer.AsMemory(0, read)); } } await zos.CloseEntryAsync(default); }
    await zos.FinishAsync(default); }
  using var zf = new ZipFile(new FileStream(zip, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true)) { Password = "x123" };
  foreach (ZipEntry e in zf) { string a; try { await using var s = zf.GetInputStream(e); a = Convert.ToHexStringLower(await SHA256.HashDataAsync(s)); } catch (Exception ex) { a = "ERR " + ex.Message; }
    var exp = H(File.OpenRead(list.First(f => Path.GetFileName(f) == e.Name)));
    if (a != exp) Console.WriteLine($"{label} [{string.Join(",", list.Select(Path.GetFileName))}] {e.Name} MISMATCH {a[..Math.Min(40,a.Length)]}"); }
  File.Delete(zip);
}
if (args[0]=="sweep") { var rnd=new Random(1); var dir=Path.Combine(Path.GetTempPath(),"qa_sweep"); Directory.CreateDirectory(dir); var bad=new List<int>();
 foreach (var size in new[]{100,4095,4096,4097,8192,16384,32768,40000,44208,44210,44212,44214,44216,44660,50000,65536,65537,100000,386803}) { var f=Path.Combine(dir,$"f{size}.png"); var b=new byte[size]; rnd.NextBytes(b); File.WriteAllBytes(f,b); var sw=new StringWriter(); var o=Console.Out; Console.SetOut(sw); await Run("S",new[]{f},false); Console.SetOut(o); Console.WriteLine($"{size}: {(sw.ToString().Contains("MISMATCH")?"MISMATCH":"ok")}"); }
 Directory.Delete(dir,true); return; }
var files = Directory.GetFiles(args[0]).OrderBy(f => f, StringComparer.Ordinal).ToArray();
IEnumerable<string[]> Perms(string[] a) { if (a.Length <= 1) { yield return a; yield break; } for (int i = 0; i < a.Length; i++) foreach (var r in Perms(a.Where((_, j) => j != i).ToArray())) yield return new[]{a[i]}.Concat(r).ToArray(); }
int n=0; foreach (var perm in Perms(files)) { n++; await Run("P"+n, perm, false); }
