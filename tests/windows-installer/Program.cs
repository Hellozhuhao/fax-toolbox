using FaxToolbox.Setup;

static void Assert(bool condition, string message) { if (!condition) throw new Exception(message); }
var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
foreach (var path in new[] { "relative", @"C:\", @"\\server\share\tool", windows, windows + @"\temp\app", @"C:\Apps\Tool:stream", "C:\\Apps\\Tool\" /DIR=x" })
{
    try { InstallEngine.ValidateDirectory(path); throw new Exception("Unsafe path accepted: " + path); }
    catch (ArgumentException) { }
}
Assert(InstallEngine.ValidateDirectory(@"C:\Apps\中文 空格\Fax Toolbox").EndsWith("Fax Toolbox"), "Custom Unicode directory rejected");
var ready = new FakePlatform { Available = true };
await DependencyWorkflow.EnsureAsync(ready, "test", _ => { });
Assert(ready.Calls.SequenceEqual(new[] { "detect" }), "Already installed runtime should skip network and installation");
var missing = new FakePlatform();
await DependencyWorkflow.EnsureAsync(missing, "test", _ => { });
Assert(missing.Calls.SequenceEqual(new[] { "detect", "download", "verify", "install", "detect" }), "Missing dependency did not follow download/verify/install/recheck order");
foreach (var stage in new[] { "download", "verify", "install", "recheck" })
{
    var fake = new FakePlatform { Fail = stage };
    try { await DependencyWorkflow.EnsureAsync(fake, "test", _ => { }); throw new Exception("Failure was ignored: " + stage); }
    catch (InvalidOperationException) { }
    if (stage == "verify") Assert(!fake.Calls.Contains("install"), "Invalid signature installer executed");
    if (stage == "download") Assert(!fake.Calls.Contains("verify") && !fake.Calls.Contains("install"), "Download failure did not stop execution");
}
if (args.Length == 1)
{
    SignatureVerifier.VerifyMicrosoft(args[0]);
    var unsigned = Path.Combine(Path.GetTempPath(), "fax-unsigned-" + Guid.NewGuid().ToString("N") + ".exe");
    try
    {
        File.WriteAllText(unsigned, "Unsigned payload must be rejected");
        try { SignatureVerifier.VerifyMicrosoft(unsigned); throw new Exception("Unsigned file passed signature check"); }
        catch (InvalidDataException) { }
    }
    finally { File.Delete(unsigned); }
}
Console.WriteLine("PASS installer path validation, dependency skip/download/failure handling and Authenticode verification");

sealed class FakePlatform : IDependencyPlatform
{
    internal bool Available;
    internal string Fail = "";
    internal List<string> Calls = [];
    public bool RuntimeInstalled() { Calls.Add("detect"); return Available; }
    public Task DownloadAsync(string destination, Action<double?> progress) { Calls.Add("download"); if (Fail == "download") throw new InvalidOperationException("download failure"); return Task.CompletedTask; }
    public void Verify(string file) { Calls.Add("verify"); if (Fail == "verify") throw new InvalidOperationException("invalid signature"); }
    public Task InstallAsync(string file) { Calls.Add("install"); if (Fail == "install") throw new InvalidOperationException("installer failure"); Available = Fail != "recheck"; return Task.CompletedTask; }
}
