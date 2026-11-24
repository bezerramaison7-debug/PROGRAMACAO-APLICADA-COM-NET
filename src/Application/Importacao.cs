using System.Buffers;
using System.Buffers.Text;
using System.Globalization;
using System.Text;

namespace Application;

public static class Importacao
{
    // Uma linha: SKU ASCII de A-Z/0-9;quantidade;preço em centavos.
    public static ItemRequest Baseline(byte[] linha)
    {
        var campos = Encoding.UTF8.GetString(linha).Split(';');
        if (campos.Length != 3 || linha.Contains((byte)0) || !int.TryParse(campos[1], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var qtd)
            || !long.TryParse(campos[2], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var preco))
            throw new FormatException("Linha inválida.");
        return Validar(campos[0], qtd, preco);
    }

    public static ItemRequest Otimizado(ReadOnlySpan<byte> linha)
    {
        var primeiro = linha.IndexOf((byte)';');
        if (primeiro < 1)
            throw new FormatException("SKU inválido.");
        var resto = linha[(primeiro + 1)..];
        var segundo = resto.IndexOf((byte)';');
        if (segundo < 1 || !Utf8Parser.TryParse(resto[..segundo], out int qtd, out var tamanhoQtd) || tamanhoQtd != segundo ||
            !Utf8Parser.TryParse(resto[(segundo + 1)..], out long preco, out var tamanhoPreco) ||
            tamanhoPreco != resto.Length - segundo - 1)
            throw new FormatException("Linha inválida.");
        return Validar(Encoding.UTF8.GetString(linha[..primeiro]), qtd, preco);
    }

    private static ItemRequest Validar(string sku, int qtd, long preco)
    {
        if (sku.Length is < 1 or > 32 || sku.Any(c => c is not (>= 'A' and <= 'Z' or >= '0' and <= '9')) ||
            qtd is < 1 or > 10_000 || preco is < 1 or > 100_000_000)
            throw new FormatException("SKU, quantidade ou preço inválido.");
        return new(sku, qtd, preco);
    }

    public static async Task<IReadOnlyList<ItemRequest>> LerAsync(Stream stream, CancellationToken token)
    {
        const int limite = 1024 * 1024;
        const int tamanhoBloco = 4096;
        var pool = ArrayPool<byte>.Shared;
        var buffer = pool.Rent(tamanhoBloco);
        var linha = new byte[128];
        try
        {
            var resultado = new List<ItemRequest>();
            int total = 0, tamanhoLinha = 0, lidos;
            while ((lidos = await stream.ReadAsync(buffer.AsMemory(0, tamanhoBloco), token)) != 0)
            {
                token.ThrowIfCancellationRequested();
                total += lidos;
                if (total > limite)
                    throw new FormatException("Lote excede 1 MiB.");
                for (var i = 0; i < lidos; i++)
                {
                    var atual = buffer[i];
                    if (atual == (byte)'\n')
                    {
                        resultado.Add(ProcessarLinha(linha, tamanhoLinha));
                        tamanhoLinha = 0;
                    }
                    else
                    {
                        if (tamanhoLinha == linha.Length)
                            throw new FormatException("Linha excede 128 bytes.");
                        linha[tamanhoLinha++] = atual;
                    }
                }
            }
            if (tamanhoLinha > 0)
                resultado.Add(ProcessarLinha(linha, tamanhoLinha));
            return resultado;
        }
        finally
        {
            pool.Return(buffer, clearArray: true);
            Array.Clear(linha);
        }
    }

    private static ItemRequest ProcessarLinha(byte[] linha, int tamanho) =>
        Otimizado(linha.AsSpan(0, tamanho > 0 && linha[tamanho - 1] == (byte)'\r' ? tamanho - 1 : tamanho));
}
