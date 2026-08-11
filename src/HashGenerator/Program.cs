using Microsoft.AspNetCore.Identity;
using ShanthiNikethan.EmployeeManagement.Modules.Admin.Data;

Console.WriteLine("=== E2E Test Account Password Hash Generator ===");
Console.WriteLine("This is a one-time, throwaway tool - not part of the real app.");
Console.WriteLine();
Console.Write("Type the password you want the E2E test account to use, then press Enter: ");
var password = Console.ReadLine();

if (string.IsNullOrWhiteSpace(password))
{
    Console.WriteLine("No password entered - stopping.");
    return;
}

// The same PasswordHasher<UserAccount> the real UserAccountService uses -
// the "user" object itself is never actually read by the hashing
// algorithm, so an empty new UserAccount() here is fine.
var hasher = new PasswordHasher<UserAccount>();
var hash = hasher.HashPassword(new UserAccount(), password);

Console.WriteLine();
Console.WriteLine("Copy the ENTIRE line below (it's one long string, all one value) into");
Console.WriteLine("the SQL script, replacing PASTE_YOUR_HASH_HERE:");
Console.WriteLine();
Console.WriteLine(hash);
Console.WriteLine();
Console.WriteLine("Press Enter to close.");
Console.ReadLine();
