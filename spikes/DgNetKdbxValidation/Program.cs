using System.Text;
using DgNet.Keepass;

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

Console.WriteLine("DgNet.Keepass validation summary");
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
    using var db = Database.Create(MasterPassword);

    var entry = new Entry
    {
        Title = EntryTitle,
        UserName = EntryUserName,
        Password = EntryPassword,
        Url = EntryUrl,
        Notes = EntryNotes
    };

    db.RootGroup.AddEntry(entry);
    db.SaveAs(vaultPath);

    return File.Exists(vaultPath) && new FileInfo(vaultPath).Length > 0;
}

static bool ReopenVault(string vaultPath, string password)
{
    using var db = Database.Open(vaultPath, password);
    var entry = db.RootGroup.Entries.SingleOrDefault();

    return entry is not null
        && entry.Title == EntryTitle
        && entry.UserName == EntryUserName
        && entry.Password == EntryPassword
        && entry.Url == EntryUrl
        && entry.Notes == EntryNotes;
}

static bool FailsToOpen(string vaultPath, string password)
{
    try
    {
        using var db = Database.Open(vaultPath, password);
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

static void DeleteIfExists(string path)
{
    if (File.Exists(path))
    {
        File.Delete(path);
    }
}

internal sealed record PlaintextScanResult(bool AnyVisible, IReadOnlyList<string> VisibleValues);
