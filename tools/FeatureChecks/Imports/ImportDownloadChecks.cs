using System.Net;
using StickerApp.Core;

internal static class ImportDownloadChecks
{
    public static async Task<int> RunAsync()
    {
        var passed = 0;
        foreach (var mode in new[] { "success", "oversized", "status", "invalid", "cancel" })
        {
            var context = new SynchronizationContext();
            var disposed = false;
            var gif = Convert.FromBase64String("R0lGODlhAQABAIAAAAAAAP///yH5BAEAAAAALAAAAAABAAEAAAIBRAA7");
            void OffUi()
            {
                if (SynchronizationContext.Current == context)
                    throw new Exception("KLIPY network access or disposal ran on the caller's UI context");
            }
            using var cancellation = new CancellationTokenSource();
            using var client = new HttpClient(new Handler(() =>
            {
                OffUi();
                var content = new StreamContent(new CheckedStream(mode == "invalid" ? new byte[12] : gif, () =>
                {
                    OffUi();
                    disposed = true;
                }, () =>
                {
                    OffUi();
                    if (mode == "cancel") { cancellation.Cancel(); cancellation.Token.ThrowIfCancellationRequested(); }
                }));
                if (mode == "oversized") content.Headers.ContentLength = LibraryStore.MaxGifImportBytes + 1;
                return new HttpResponseMessage(mode == "status" ? HttpStatusCode.BadGateway : HttpStatusCode.OK) { Content = content };
            }));
            Task<byte[]> operation;
            var previous = SynchronizationContext.Current;
            try
            {
                SynchronizationContext.SetSynchronizationContext(context);
                operation = KlipyGifDownloadClient.DownloadAsync(client, "https://static1.klipy.com/test.gif", cancellation.Token);
            }
            finally { SynchronizationContext.SetSynchronizationContext(previous); }
            try
            {
                var bytes = await operation;
                if (mode != "success" || !bytes.SequenceEqual(gif)) throw new Exception("Unexpected download success");
            }
            catch (InvalidDataException) when (mode is "oversized" or "invalid") { }
            catch (HttpRequestException) when (mode == "status") { }
            catch (OperationCanceledException) when (mode == "cancel") { }
            if (!disposed) throw new Exception("KLIPY response stream was not disposed: " + mode);
            Console.WriteLine("PASS KLIPY network and disposal stay off UI context: " + mode);
            passed++;
        }
        foreach (var size in new long?[] { 3_000_000, null })
        {
            using var headClient = new HttpClient(new HeaderHandler(size));
            var actual = await KlipyGifDownloadClient.InspectSizeAsync(headClient, "https://static.klipy.com/headers.gif");
            if (actual != size) throw new Exception("HEAD file-size inspection lost known/unknown metadata");
            passed++;
        }
        Console.WriteLine("PASS native GIF size preflight uses only HEAD and never reads the media body");
        foreach (var id in new[] { "ozD1q2COi", "31w30freR", "ENue35Kpm" })
        {
            var text = @"Uite ce panou am creat pe Pinterest! https\://pin.it/" + id;
            if (PinterestShareLink.Parse(text).AbsoluteUri != "https://pin.it/" + id)
                throw new Exception("Escaped Pinterest share text was not parsed");
            passed++;
        }
        return passed;
    }

    private sealed class HeaderHandler(long? size) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            if (request.Method != HttpMethod.Head) throw new Exception("Size preflight tried to download the GIF");
            var content = new HeaderOnlyContent();
            content.Headers.ContentLength = size;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
        }
    }
    private sealed class HeaderOnlyContent : HttpContent
    {
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) => throw new Exception("Preflight read a GIF body");
        protected override bool TryComputeLength(out long length) { length = 0; return false; }
    }

    private sealed class Handler(Func<HttpResponseMessage> response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(response());
    }

    private sealed class CheckedStream(byte[] bytes, Action dispose, Action read) : MemoryStream(bytes)
    {
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            read();
            return base.ReadAsync(buffer, cancellationToken);
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing) dispose();
            base.Dispose(disposing);
        }
    }
}
