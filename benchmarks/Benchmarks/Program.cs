using System.Text;
using Application;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;

BenchmarkSwitcher.FromAssembly(typeof(ParserBenchmarks).Assembly).Run(args);

[MemoryDiagnoser]
[ShortRunJob]
public class ParserBenchmarks
{
    private byte[][] linhas = [];

    [Params(16, 1600)]
    public int Tamanho { get; set; }

    [GlobalSetup]
    public void Setup() => linhas = Enumerable.Range(0, Tamanho)
        .Select(i => Encoding.UTF8.GetBytes($"SKU{i};2;300")).ToArray();

    [Benchmark(Baseline = true)]
    public long Baseline() => linhas.Sum(l => Importacao.Baseline(l).PrecoCentavos);

    [Benchmark]
    public long Span() => linhas.Sum(l => Importacao.Otimizado(l).PrecoCentavos);
}
