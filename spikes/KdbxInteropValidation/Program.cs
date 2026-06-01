using System.Security.Cryptography;
using DgNet.Keepass;
using KeePassLib;
using KeePassLib.Keys;
using KeePassLib.Security;
using KeePassLib.Serialization;

const string MasterPassword = "correct horse battery staple - spike only";
const string EntryTitle = "Interop Login";
const string EntryUserName = "interop.user@example.test";
const string EntryPassword = "SPK-INTEROP-13f6f1a";
const string EntryUrl = "https://example.test/interop";
const string EntryNotes = "KDBX interop validation entry";

var outputDirectory = Path.Combine(AppContext.BaseDirectory, "spike-output");
Directory.CreateDirectory(outputDirectory);

var dgNetVaultPath = Path.Combine(outputDirectory, "dgnet-written.kdbx");
var keePassLibVaultPath = Path.Combine(outputDirectory, "keepasslib-written.kdbx");

DeleteIfExists(dgNetVaultPath);
DeleteIfExists(keePassLibVaultPath);

CreateWithDgNet(dgNetVaultPath);
CreateWithKeePassLib(keePassLibVaultPath);

var dgNetWrittenReadByKeePassLib = ReadWithKeePassLib(dgNetVaultPath);
var keePassLibWrittenReadByDgNet = ReadWithDgNet(keePassLibVaultPath);
var passed = dgNetWrittenReadByKeePassLib && keePassLibWrittenReadByDgNet;

Console.WriteLine("KDBX interop validation summary");
Console.WriteLine($"DgNet.Keepass write -> KeePassLib.Standard read: {(dgNetWrittenReadByKeePassLib ? "PASS" : "FAIL")}");
Console.WriteLine($"KeePassLib.Standard write -> DgNet.Keepass read: {(keePassLibWrittenReadByDgNet ? "PASS" : "FAIL")}");
Console.WriteLine($"Overall: {(passed ? "PASS" : "FAIL")}");

Environment.ExitCode = passed ? 0 : 1;

static void CreateWithDgNet(string vaultPath)
{
    var settings = new Settings
    {
        Format = DgNet.Keepass.KdbxFormat.Kdbx3,
        Cipher = CipherAlgorithm.ChaCha20,
        Kdf = new AesKdf(RandomNumberGenerator.GetBytes(32), 100_000UL),
    };

    using var db = Database.Create(MasterPassword, settings);

    db.RootGroup.AddEntry(new Entry
    {
        Title = EntryTitle,
        UserName = EntryUserName,
        Password = EntryPassword,
        Url = EntryUrl,
        Notes = EntryNotes
    });

    db.SaveAs(vaultPath);
}

static bool ReadWithDgNet(string vaultPath)
{
    try
    {
        using var db = Database.Open(vaultPath, MasterPassword);
        var entry = db.RootGroup.Entries.SingleOrDefault();

        return entry is not null
            && entry.Title == EntryTitle
            && entry.UserName == EntryUserName
            && entry.Password == EntryPassword
            && entry.Url == EntryUrl
            && entry.Notes == EntryNotes;
    }
    catch
    {
        return false;
    }
}

static void CreateWithKeePassLib(string vaultPath)
{
    var db = new PwDatabase();
    var io = IOConnectionInfo.FromPath(vaultPath);

    db.New(io, BuildKeePassLibKey(MasterPassword));

    var entry = new PwEntry(true, true);
    entry.Strings.Set(PwDefs.TitleField, new ProtectedString(false, EntryTitle));
    entry.Strings.Set(PwDefs.UserNameField, new ProtectedString(false, EntryUserName));
    entry.Strings.Set(PwDefs.PasswordField, new ProtectedString(true, EntryPassword));
    entry.Strings.Set(PwDefs.UrlField, new ProtectedString(false, EntryUrl));
    entry.Strings.Set(PwDefs.NotesField, new ProtectedString(false, EntryNotes));

    db.RootGroup.AddEntry(entry, true);
    db.Save(null);
    db.Close();
}

static bool ReadWithKeePassLib(string vaultPath)
{
    var db = new PwDatabase();
    try
    {
        db.Open(IOConnectionInfo.FromPath(vaultPath), BuildKeePassLibKey(MasterPassword), null);
        var entry = db.RootGroup.GetEntries(true).SingleOrDefault();

        return entry is not null
            && Read(entry, PwDefs.TitleField) == EntryTitle
            && Read(entry, PwDefs.UserNameField) == EntryUserName
            && Read(entry, PwDefs.PasswordField) == EntryPassword
            && Read(entry, PwDefs.UrlField) == EntryUrl
            && Read(entry, PwDefs.NotesField) == EntryNotes;
    }
    catch
    {
        return false;
    }
    finally
    {
        db.Close();
    }
}

static KeePassLib.Keys.CompositeKey BuildKeePassLibKey(string password)
{
    var key = new KeePassLib.Keys.CompositeKey();
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
