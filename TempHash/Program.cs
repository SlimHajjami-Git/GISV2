string password = "Admin@2026";
string hash = BCrypt.Net.BCrypt.HashPassword(password);
Console.WriteLine($"Password: {password}");
Console.WriteLine($"Hash: {hash}");
