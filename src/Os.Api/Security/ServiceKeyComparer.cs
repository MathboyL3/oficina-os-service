using System.Security.Cryptography;
using System.Text;

namespace Os.Api.Security;

/// <summary>Compara a chave de serviço em tempo constante. Chave esperada vazia nunca é aceita.</summary>
public static class ServiceKeyComparer
{
    public static bool IsValid(string? expected, string? provided)
    {
        if (string.IsNullOrEmpty(expected) || string.IsNullOrEmpty(provided)) return false;
        var a = SHA256.HashData(Encoding.UTF8.GetBytes(expected));
        var b = SHA256.HashData(Encoding.UTF8.GetBytes(provided));
        // Hash antes de comparar: FixedTimeEquals exige tamanhos iguais e assim o tamanho da chave não vaza.
        return CryptographicOperations.FixedTimeEquals(a, b);
    }
}