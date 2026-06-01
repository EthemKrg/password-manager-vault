using PasswordManager.Core;
using PasswordManager.Infrastructure;

const string MasterPassword = "correct horse battery staple - validation only";
const string WrongPassword = "wrong password - validation only";

var outputDirectory = Path.Combine(AppContext.BaseDirectory, "validation-output");
Directory.CreateDirectory(outputDirectory);

var vaultPath = Path.Combine(outputDirectory, "service-validation.kdbx");
if (File.Exists(vaultPath))
{
    File.Delete(vaultPath);
}

var service = new DgNetVaultService();
var createResult = await service.CreateAsync(vaultPath, MasterPassword);

var account = DgNetVaultService.CreateEntry(new AccountEntryDraft(
    "GitHub",
    "https://github.com",
    "dev@example.test",
    "SPK-SERVICE-VALIDATION-5c22",
    "Initial service validation entry",
    ["dev", "source"]));

var emptyLoadResult = await service.LoadAsync(vaultPath, MasterPassword);
var saveSnapshot = emptyLoadResult.Value?.Add(account) ?? VaultSnapshot.Empty.Add(account);
var saveResult = await service.SaveAsync(vaultPath, MasterPassword, saveSnapshot);
var loadResult = await service.LoadAsync(vaultPath, MasterPassword);
var wrongPasswordResult = await service.LoadAsync(vaultPath, WrongPassword);

var loadedAccount = loadResult.Value?.Entries.SingleOrDefault();
var roundtripPassed = loadedAccount is not null
    && loadedAccount.Id == account.Id
    && loadedAccount.ServiceName == account.ServiceName
    && loadedAccount.WebsiteUrl == account.WebsiteUrl
    && loadedAccount.UsernameOrEmail == account.UsernameOrEmail
    && loadedAccount.Password == account.Password
    && loadedAccount.Notes == account.Notes
    && loadedAccount.Tags.SequenceEqual(account.Tags)
    && loadedAccount.IsFavorite == account.IsFavorite
    && loadedAccount.CreatedAtUtc == account.CreatedAtUtc
    && loadedAccount.UpdatedAtUtc == account.UpdatedAtUtc
    && loadedAccount.PasswordChangedAtUtc == account.PasswordChangedAtUtc;

var passed = createResult.Succeeded
    && emptyLoadResult.Succeeded
    && saveResult.Succeeded
    && loadResult.Succeeded
    && !wrongPasswordResult.Succeeded
    && roundtripPassed;

Console.WriteLine("Infrastructure validation summary");
Console.WriteLine($"Create empty vault: {(createResult.Succeeded ? "PASS" : "FAIL")}");
Console.WriteLine($"Load empty vault before save: {(emptyLoadResult.Succeeded ? "PASS" : "FAIL")}");
Console.WriteLine($"Save snapshot: {(saveResult.Succeeded ? "PASS" : "FAIL")}");
Console.WriteLine($"Load snapshot: {(loadResult.Succeeded ? "PASS" : "FAIL")}");
Console.WriteLine($"Roundtrip account fields: {(roundtripPassed ? "PASS" : "FAIL")}");
Console.WriteLine($"Wrong master password fails: {(!wrongPasswordResult.Succeeded ? "PASS" : "FAIL")}");
Console.WriteLine($"Overall: {(passed ? "PASS" : "FAIL")}");

Environment.ExitCode = passed ? 0 : 1;
