using NUnit.Framework;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Brows.Composition.ImportAgents;

[TestFixture]
internal sealed class ImportStateTest {
    private static ImportState State(IExport export, Resource resource) {
        return new ImportState(new ImportAgent(null), variables: null, new Factory(export, resource));
    }

    [Test]
    public void Ready_ExportInitThrows_DisposesResources() {
        var resource = new Resource();
        var state = State(new Export { Initialize = token => throw new InvalidOperationException() }, resource);
        Assert.That(async () => await state.Ready(CancellationToken.None), Throws.InvalidOperationException);
        Assert.That(resource.DisposeCount, Is.EqualTo(1));
    }

    [Test]
    public void Ready_Canceled_DisposesResources() {
        var resource = new Resource();
        var state = State(new Export { Initialize = token => Task.Delay(Timeout.Infinite, token) }, resource);
        using (var cancellation = new CancellationTokenSource()) {
            var ready = state.Ready(cancellation.Token);
            cancellation.Cancel();
            Assert.That(async () => await ready, Throws.InstanceOf<OperationCanceledException>());
        }
        Assert.That(resource.DisposeCount, Is.EqualTo(1));
    }

    [Test]
    public async Task Dispose_WhileReadyInFlight_KillsAndDisposesOnce() {
        var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var resource = new Resource();
        var export = new Export { Initialize = token => gate.Task };
        var state = State(export, resource);
        var first = state.Ready(CancellationToken.None);
        var second = state.Ready(CancellationToken.None);
        state.Dispose();
        gate.SetResult(true);
        using (Assert.EnterMultipleScope()) {
            Assert.That(async () => await first, Throws.InstanceOf<ObjectDisposedException>());
            Assert.That(async () => await second, Throws.InstanceOf<ObjectDisposedException>());
        }
        using (Assert.EnterMultipleScope()) {
            Assert.That(export.KillCount, Is.EqualTo(1));
            Assert.That(resource.DisposeCount, Is.EqualTo(1));
        }
    }

    [Test]
    public async Task Dispose_AfterReady_KillsAndDisposesOnce() {
        var resource = new Resource();
        var export = new Export();
        var state = State(export, resource);
        await state.Ready(CancellationToken.None);
        state.Dispose();
        using (Assert.EnterMultipleScope()) {
            Assert.That(export.KillCount, Is.EqualTo(1));
            Assert.That(resource.DisposeCount, Is.EqualTo(1));
        }
    }

    private sealed class Resource : IDisposable {
        private int Count;

        public int DisposeCount => Volatile.Read(ref Count);

        void IDisposable.Dispose() {
            Interlocked.Increment(ref Count);
        }
    }

    /*
     * Tests that compose the test assembly also create this export,
     * so its default construction must do nothing.
     */
    private sealed class Export : IExportAndInit, IExportAndKill {
        private int Count;

        public int KillCount => Volatile.Read(ref Count);
        public Func<CancellationToken, Task> Initialize { get; set; }

        Task IExportAndInit.Init(IExportContext context, CancellationToken token) {
            return Initialize?.Invoke(token) ?? Task.CompletedTask;
        }

        void IExportAndKill.Kill() {
            Interlocked.Increment(ref Count);
        }
    }

    private sealed class Factory : ImportCollectionFactory {
        private readonly IExport Export;
        private readonly IDisposable Resource;

        public Factory(IExport export, IDisposable resource) {
            Export = export;
            Resource = resource;
        }

        public override Task<ImportCollection> Create(CancellationToken token) {
            return Task.FromResult(new ImportCollection([Export], [Resource]));
        }
    }
}
