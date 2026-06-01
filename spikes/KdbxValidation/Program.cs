using System.Text;
using KeePassLib;
using KeePassLib.Keys;
using KeePassLib.Security;
using KeePassLib.Serialization;

const string MasterPassword = "correct horse battery staple - spike only";
const string WrongPassword = "wrong master password - spike only";
const string EntryTitle = "Spike Login";
const string EntryUserName = "spike.user@example.test";
const string EntryPassword = "SPK-PASS-7d4f2b1c9a";
const string EntryUrl = "https://example.test/login";
const string EntryNotes = "KDBX validation entry";

var outputDirectory = Path.Combine(AppContext.BaseDirectory, "spike-output");
Directory.CreateDirectory(outputDirectory);

var vaultPath = Path.Combine(outputDirectory, "validation.kdbx");
var corruptedVaultPath = Path.Combine(outputDirectory, "validation-corrupted.kdbx");

DeleteIfExists(vaultPath);
DeleteIfExists(corruptedVaultPath);

var createResult = CreateVault(vaultPath);
var reopenResult = ReopenVault(vaultPath, MasterPassword);
var wrongPasswordFails = FailsToOpen(vaultPath, WrongPassword);
var plaintextScan = ScanForPlaintext(vaultPath, [EntryTitle, EntryUserName, EntryPassword, EntryUrl, EntryNotes]);
var corruptionFails = CorruptAndVerifyFailure(vaultPath, corruptedVaultPath);
var outputFiles = Directory.GetFiles(outputDirectory)
    .Select(Path.GetFileName)
    .Order(StringComparer.OrdinalIgnoreCase)
    .ToArray();

var passed = createResult
    && reopenResult
    && wrongPasswordFails
    && !plaintextScan.AnyVisible
    && corruptionFails;

Console.WriteLine("KDBX validation summary");
Console.WriteLine($"Create and save vault: {(createResult ? "PASS" : "FAIL")}");
Console.WriteLine($"Reopen and read entry: {(reopenResult ? "PASS" : "FAIL")}");
Console.WriteLine($"Wrong master password fails: {(wrongPasswordFails ? "PASS" : "FAIL")}");
Console.WriteLine($"Plaintext scan clean: {(!plaintextScan.AnyVisible ? "PASS" : "FAIL")}");
Console.WriteLine($"Corruption/integrity failure detected: {(corruptionFails ? "PASS" : "FAIL")}");
Console.WriteLine($"Output files: {string.Join(", ", outputFiles)}");
Console.WriteLine($"Overall: {(passed ? "PASS" : "FAIL")}");

Environment.ExitCode = passed ? 0 : 1;

static bool CreateVault(string vaultPath)
{
    var db = new PwDatabase();
    var key = BuildKey(MasterPassword);
    var io = IOConnectionInfo.FromPath(vaultPath);

    db.New(io, key);

    var entry = new PwEntry(true, true);
    entry.Strings.Set(PwDefs.TitleField, new ProtectedString(false, EntryTitle));
    entry.Strings.Set(PwDefs.UserNameField, new ProtectedString(false, EntryUserName));
    entry.Strings.Set(PwDefs.PasswordField, new ProtectedString(true, EntryPassword));
    entry.Strings.Set(PwDefs.UrlField, new ProtectedString(false, EntryUrl));
    entry.Strings.Set(PwDefs.NotesField, new ProtectedString(false, EntryNotes));

    db.RootGroup.AddEntry(entry, true);
    db.Save(null);
    db.Close();

    return File.Exists(vaultPath) && new FileInfo(vaultPath).Length > 0;
}

static bool ReopenVault(string vaultPath, string password)
{
    var db = new PwDatabase();
    try
    {
        db.Open(IOConnectionInfo.FromPath(vaultPath), BuildKey(password), null);

        var entry = db.RootGroup.GetEntries(true).SingleOrDefault();
        if (entry is null)
        {
            return false;
        }

        var titleMatches = Read(entry, PwDefs.TitleField) == EntryTitle;
        var userMatches = Read(entry, PwDefs.UserNameField) == EntryUserName;
        var passwordMatches = Read(entry, PwDefs.PasswordField) == EntryPassword;
        var urlMatches = Read(entry, PwDefs.UrlField) == EntryUrl;
        var notesMatches = Read(entry, PwDefs.NotesField) == EntryNotes;

        return titleMatches && userMatches && passwordMatches && urlMatches && notesMatches;
    }
    finally
    {
        db.Close();
    }
}

static bool FailsToOpen(string vaultPath, string password)
{
    try
    {
        var db = new PwDatabase();
        db.Open(IOConnectionInfo.FromPath(vaultPath), BuildKey(password), null);
        db.Close();
        return false;
    }
    catch
    {
        return true;
    }
}

static PlaintextScanResult ScanForPlaintext(string filePath, IReadOnlyCollection<string> values)
{
    var bytes = File.ReadAllBytes(filePath);
    var text = Encoding.UTF8.GetString(bytes);
    var visibleValues = values
        .Where(value => text.Contains(value, StringComparison.Ordinal))
        .ToArray();

    return new PlaintextScanResult(visibleValues.Length > 0, visibleValues);
}

static bool CorruptAndVerifyFailure(string vaultPath, string corruptedVaultPath)
{
    var bytes = File.ReadAllBytes(vaultPath);
    bytes[^1] ^= 0xFF;
    File.WriteAllBytes(corruptedVaultPath, bytes);

    return FailsToOpen(corruptedVaultPath, MasterPassword);
}

static CompositeKey BuildKey(string password)
{
    var key = new CompositeKey();
    key.AddUserKey(new KcpPassword(password));
    return key;
}

static string Read(PwEntry entry, string field)
{
    return entry.Strings.Get(field)?.ReadString() ?? string.Empty;
}

static void DeleteIfExists(string path)
{
    if (File.Exists(path))
    {
        File.Delete(path);
    }
}

internal sealed record PlaintextScanResult(bool AnyVisible, IReadOnlyList<string> VisibleValues);
