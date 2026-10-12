using System.Text.Json;
using IntelligenceKit.Core.Models;
using IntelligenceKit.Server.Data;
using IntelligenceKit.Server.Ingest;
using IntelligenceKit.Server.Projects;
using Microsoft.EntityFrameworkCore;

namespace IntelligenceKit.Server.Demo;

/// <summary>
/// Fills an empty database with two weeks of realistic sample data for a demo
/// instance (<c>Demo:Seed=true</c>): a registered "demo-shop" project, three
/// releases (the middle one ships a regression that the last one fixes), grouped
/// issues with triage states, sessions, performance spans and user feedback.
/// Events are inserted un-fingerprinted and then grouped by the real
/// <see cref="IssueBackfill"/>, so issues come from the same logic as live ingest.
/// Deterministic (fixed seed) so screenshots and docs stay stable.
/// </summary>
public sealed class DemoSeeder
{
    public const string ProjectId = "demo-shop";

    private sealed record Problem(string Type, string Message, string Stack, double Weight, string[] Releases, string Platform = "any");

    private static readonly (string Release, int FromDay, int ToDay)[] Releases =
    [
        ("2.3.0", 14, 8),
        ("2.4.0", 8, 3),   // introduces the payment crash
        ("2.4.1", 3, 0),   // fixes it
    ];

    private static readonly Problem[] Problems =
    [
        new("System.NullReferenceException", "Object reference not set to an instance of an object.",
            "   at DemoShop.Pages.CartPage.OnPayClicked(Object sender, EventArgs e)\n   at Microsoft.Maui.Controls.Button.SendClicked()", 6, ["2.4.0"]),
        new("System.Net.Http.HttpRequestException", "Connection refused (api.demo-shop.io:443)",
            "   at System.Net.Http.HttpConnectionPool.ConnectAsync(HttpRequestMessage request)\n   at DemoShop.Services.OrdersApi.GetOrdersAsync(Int32 page)", 5, ["2.3.0", "2.4.0", "2.4.1"]),
        new("ApplicationNotResponding", "The UI thread was blocked for more than 5 s.",
            "   at android.database.sqlite.SQLiteConnection.nativeExecute(Native Method)\n   at DemoShop.Data.CatalogCache.Rebuild()", 2, ["2.3.0", "2.4.0", "2.4.1"], "Android"),
        new("System.InvalidOperationException", "Sequence contains no elements",
            "   at System.Linq.ThrowHelper.ThrowNoElementsException()\n   at DemoShop.ViewModels.CheckoutViewModel.<LoadShippingAsync>d__14.MoveNext()", 3, ["2.3.0", "2.4.0"]),
        new("java.lang.IllegalStateException", "Fragment ProductDetailFragment not attached to a context.",
            "java.lang.IllegalStateException: Fragment ProductDetailFragment not attached to a context.\n\tat androidx.fragment.app.Fragment.requireContext(Fragment.java:972)\n\tat com.demoshop.ProductDetailFragment.onImageLoaded(ProductDetailFragment.java:88)", 1.5, ["2.4.0", "2.4.1"], "Android"),
        new("Foundation.MonoTouchException", "Objective-C exception thrown. Name: NSInternalInconsistencyException",
            "   at ObjCRuntime.Runtime.ThrowNSException(IntPtr ns_exception)\n   at DemoShop.iOS.Handlers.MapViewHandler.UpdateRegion()", 1, ["2.3.0", "2.4.0", "2.4.1"], "iOS"),
        new("System.ObjectDisposedException", "Cannot access a disposed object. Object name: 'SQLiteConnection'.",
            "   at SQLite.SQLiteConnection.Execute(String query)\n   at DemoShop.Data.FavoritesRepository.SaveAsync(Product product)", 1, ["2.4.1"]),
        new("System.TimeoutException", "The operation has timed out.",
            "   at DemoShop.Services.PaymentGateway.AuthorizeAsync(PaymentRequest request)", 2, ["2.3.0", "2.4.0", "2.4.1"]),
    ];

    private static readonly (string Model, string Maker, string Os, string Platform)[] Devices =
    [
        ("Pixel 8", "Google", "Android 15", "Android"),
        ("Galaxy S24", "Samsung", "Android 14", "Android"),
        ("Galaxy A54", "Samsung", "Android 14", "Android"),
        ("Redmi Note 13", "Xiaomi", "Android 13", "Android"),
        ("iPhone 15", "Apple", "iOS 18.2", "iOS"),
        ("iPhone 13", "Apple", "iOS 17.6", "iOS"),
        ("iPad Air", "Apple", "iPadOS 18.1", "iOS"),
    ];

    private static readonly string[] Pages = ["HomePage", "CatalogPage", "ProductPage", "CartPage", "CheckoutPage", "ProfilePage"];
    private static readonly string[] Tenants = ["acme", "globex", "initech", "umbrella"];

    private readonly IntelligenceDbContext _db;
    private readonly IssueBackfill _backfill;
    private readonly Random _random = new(42);

    // Separate stream for the timeline stories, so adding them doesn't reshuffle the rest of the demo data.
    private readonly Random _stories = new(7);

    public DemoSeeder(IntelligenceDbContext db, IssueBackfill backfill)
    {
        _db = db;
        _backfill = backfill;
    }

    /// <summary>Seeds only when the database has no events yet. Returns whether it seeded.</summary>
    public async Task<bool> SeedIfEmptyAsync(CancellationToken ct = default)
    {
        if (await _db.Events.AnyAsync(ct))
            return false;

        var now = DateTime.UtcNow;

        if (!await _db.Projects.AnyAsync(p => p.ProjectId == ProjectId, ct))
        {
            _db.Projects.Add(new Project
            {
                Id = Guid.NewGuid(),
                ProjectId = ProjectId,
                ProjectKey = "demo-key",
                Name = "Demo Shop (MAUI)",
                ReadKeyHash = ProjectKeys.Hash(ProjectKeys.NewReadKey()),
                CreatedAt = now.AddDays(-15),
            });
        }

        SeedSessions(now);
        var crashIds = SeedEvents(now);
        SeedSpans(now);
        SeedTimelines(now);
        await _db.SaveChangesAsync(ct);
        _db.ChangeTracker.Clear();

        await _backfill.RunAsync(ct);
        await TriageAsync(now, ct);
        await SeedFeedbackAsync(crashIds, now, ct);
        return true;
    }

    private List<Guid> SeedEvents(DateTime now)
    {
        var crashes = new List<Guid>();
        var totalWeight = Problems.Sum(p => p.Weight);

        for (var i = 0; i < 1400; i++)
        {
            var (release, at) = RandomReleaseMoment(now);
            var candidates = Problems.Where(p => p.Releases.Contains(release)).ToArray();
            var problem = Pick(candidates, candidates.Sum(p => p.Weight));
            var device = PickDevice(problem.Platform);
            var user = $"u-{_random.Next(1, 240):D3}";

            var e = new StoredEvent
            {
                Id = Guid.NewGuid(),
                ProjectId = ProjectId,
                ProjectKey = "demo-key",
                ApplicationName = "Demo Shop",
                ApplicationVersion = release,
                Release = release,
                Environment = _random.NextDouble() < 0.9 ? "production" : "staging",
                Platform = device.Platform,
                DeviceName = device.Model,
                DeviceModel = device.Model,
                Manufacturer = device.Maker,
                OperatingSystem = device.Os,
                UserId = _random.NextDouble() < 0.8 ? user : null,
                EventType = "Exception",
                Level = "Error",
                ExceptionType = problem.Type,
                ExceptionMessage = problem.Message,
                ExceptionJson = JsonSerializer.Serialize(new ExceptionInfo { Type = problem.Type, Message = problem.Message, StackTrace = problem.Stack }),
                BreadcrumbsJson = JsonSerializer.Serialize(Breadcrumbs(at)),
                TagsJson = JsonSerializer.Serialize(new Dictionary<string, string>
                {
                    ["tenant"] = Tenants[_random.Next(Tenants.Length)],
                    ["plan"] = _random.NextDouble() < 0.3 ? "pro" : "free",
                }),
                DeviceRuntimeJson = JsonSerializer.Serialize(new DeviceRuntime
                {
                    MemoryUsedBytes = _random.Next(180, 900) * 1024L * 1024,
                    BatteryLevel = Math.Round(_random.NextDouble(), 2),
                    NetworkAccess = _random.NextDouble() < 0.85 ? "Internet" : "ConstrainedInternet",
                    Screen = Pages[_random.Next(Pages.Length)],
                }),
                Fingerprint = "", // grouped by IssueBackfill below
                Timestamp = at,
                ReceivedAt = at.AddSeconds(_random.Next(1, 30)),
            };
            _db.Events.Add(e);

            if (problem.Type == "System.NullReferenceException" && crashes.Count < 3)
                crashes.Add(e.Id);
        }

        // A few logged warnings, grouped by message.
        for (var i = 0; i < 120; i++)
        {
            var (release, at) = RandomReleaseMoment(now);
            var device = PickDevice("any");
            _db.Events.Add(new StoredEvent
            {
                Id = Guid.NewGuid(), ProjectId = ProjectId, ProjectKey = "demo-key",
                ApplicationName = "Demo Shop", ApplicationVersion = release, Release = release, Environment = "production",
                Platform = device.Platform, DeviceName = device.Model, DeviceModel = device.Model, Manufacturer = device.Maker,
                OperatingSystem = device.Os, EventType = "Log", Level = "Warning",
                Message = $"Image cache over budget ({_random.Next(51, 90)} MB), evicting",
                Fingerprint = "", Timestamp = at, ReceivedAt = at,
            });
        }

        return crashes;
    }

    private void SeedSessions(DateTime now)
    {
        foreach (var (release, fromDay, toDay) in Releases)
        {
            // Newer releases ramp up; the buggy 2.4.0 crashes more often.
            var crashRate = release == "2.4.0" ? 0.034 : release == "2.4.1" ? 0.006 : 0.011;
            var sessions = (fromDay - toDay) * 320;
            for (var i = 0; i < sessions; i++)
            {
                var started = now.AddDays(-fromDay).AddMinutes(_random.NextDouble() * (fromDay - toDay) * 24 * 60);
                if (started > now)
                    continue;
                var device = PickDevice("any");
                var crashed = _random.NextDouble() < crashRate;
                var user = $"u-{_random.Next(1, 240):D3}";
                _db.Sessions.Add(new AppSession
                {
                    Id = Guid.NewGuid(),
                    ProjectId = ProjectId,
                    Release = release,
                    Environment = "production",
                    Platform = device.Platform,
                    DeviceModel = device.Model,
                    Manufacturer = device.Maker,
                    OperatingSystem = device.Os,
                    DistinctId = "install-" + user,
                    UserId = _random.NextDouble() < 0.7 ? user : null,
                    Started = started,
                    LastUpdate = started.AddMinutes(_random.Next(1, 25)),
                    Status = crashed ? "Crashed" : "Exited",
                    Errors = crashed || _random.NextDouble() < 0.05 ? _random.Next(1, 3) : 0,
                    DurationSeconds = _random.Next(20, 1500),
                    Sequence = 2,
                });
            }
        }
    }

    /// <summary>
    /// The most recent sessions recorded a timeline (<c>EnableSessionTimeline</c>): a shopper browsing,
    /// adding to the cart and paying. One of them crashes on "Pay", with the crash on its timeline.
    /// </summary>
    private void SeedTimelines(DateTime now)
    {
        var recent = _db.Sessions.Local
            .Where(s => s.Started > now.AddDays(-1))
            .OrderByDescending(s => s.Started)
            .Take(12)
            .ToList();

        for (var i = 0; i < recent.Count; i++)
        {
            var session = recent[i];
            var crash = i == 1;
            if (crash)
            {
                session.Status = "Crashed";
                session.Errors = Math.Max(1, session.Errors);
            }
            SeedTimeline(session, crash);
        }
    }

    private void SeedTimeline(AppSession session, bool crash)
    {
        var at = session.Started;
        var sequence = 0L;
        var batch = Guid.NewGuid();
        var product = _stories.Next(100, 999);

        void Step(string category, string message, string level = "Information", Dictionary<string, string>? data = null)
        {
            at = at.AddMilliseconds(_stories.Next(700, 6000));
            if (sequence > 0 && sequence % 10 == 0)
                batch = Guid.NewGuid();
            _db.TimelineEntries.Add(new TimelineEntry
            {
                Id = Guid.NewGuid(), ProjectId = ProjectId, SessionId = session.Id, BatchId = batch,
                Sequence = sequence++, Timestamp = at, Category = category, Message = message, Level = level,
                DataJson = data is null ? null : JsonSerializer.Serialize(data), ReceivedAt = at.AddSeconds(3),
            });
        }

        void Http(string method, string path, int status, int ms)
            => Step("http", $"{method} api.demo-shop.io{path}", status >= 500 ? "Error" : "Information",
                new() { ["status_code"] = status.ToString(), ["duration_ms"] = ms.ToString(), ["response_size"] = _stories.Next(400, 9000).ToString() });

        Step("navigation", "HomePage");
        Http("GET", "/catalog", 200, _stories.Next(120, 400));
        Step("navigation", "CatalogPage");
        Step("user", "product_tapped", data: new() { ["product"] = $"sku-{product}" });
        Step("navigation", "ProductPage");
        Http("GET", "/products/{id}", 200, _stories.Next(90, 300));
        Step("user", "add_to_cart_clicked");
        Step("navigation", "CartPage");
        Step("log", "Cart total recalculated", data: new() { ["items"] = _stories.Next(1, 5).ToString() });
        Step("user", "pay_clicked");

        if (crash)
        {
            Http("POST", "/payments/authorize", 502, 2412);
            Step("log", "Payment gateway returned 502, retrying with saved card", "Warning");
            at = at.AddMilliseconds(400);
            var problem = Problems[0]; // the CartPage.OnPayClicked NullReferenceException
            _db.Events.Add(new StoredEvent
            {
                Id = Guid.NewGuid(), ProjectId = ProjectId, ProjectKey = "demo-key",
                ApplicationName = "Demo Shop", ApplicationVersion = session.Release, Release = session.Release,
                Environment = session.Environment, Platform = session.Platform, DeviceName = session.DeviceModel,
                DeviceModel = session.DeviceModel, Manufacturer = session.Manufacturer, OperatingSystem = session.OperatingSystem,
                UserId = session.UserId, SessionId = session.Id, EventType = "Exception", Level = "Error",
                ExceptionType = problem.Type, ExceptionMessage = problem.Message,
                ExceptionJson = JsonSerializer.Serialize(new ExceptionInfo { Type = problem.Type, Message = problem.Message, StackTrace = problem.Stack }),
                Fingerprint = "", Timestamp = at, ReceivedAt = at.AddSeconds(20),
            });
        }
        else
        {
            Http("POST", "/payments/authorize", 200, _stories.Next(300, 900));
            Step("navigation", "CheckoutPage");
            Step("user", "confirm_order_clicked");
            Http("POST", "/orders", 201, _stories.Next(150, 500));
            Step("navigation", "OrderConfirmationPage");
        }

        session.TimelineEntries = (int)sequence;
        session.LastActivity = at;
        if (session.LastUpdate < at)
            session.LastUpdate = at;
    }

    private void SeedSpans(DateTime now)
    {
        var endpoints = new (string Name, double Median)[]
        {
            ("GET api.demo-shop.io/catalog", 180), ("GET api.demo-shop.io/products/{id}", 120),
            ("POST api.demo-shop.io/cart/{id}/items", 240), ("POST api.demo-shop.io/payments", 900),
            ("GET api.demo-shop.io/orders", 320),
        };

        for (var i = 0; i < 3000; i++)
        {
            var (release, at) = RandomReleaseMoment(now);
            var device = PickDevice("any");
            string op, name;
            double median;
            int? status = null;
            var success = true;

            switch (_random.Next(10))
            {
                case 0:
                    (op, name, median) = ("app.start", "cold", device.Platform == "Android" ? 1400 : 950);
                    break;
                case < 5:
                    (op, name) = ("ui.load", Pages[_random.Next(Pages.Length)]);
                    median = name == "CatalogPage" ? 650 : 220;
                    break;
                default:
                    var ep = endpoints[_random.Next(endpoints.Length)];
                    (op, name, median) = ("http.client", ep.Name, ep.Median);
                    success = _random.NextDouble() > (ep.Name.Contains("payments") ? 0.04 : 0.01);
                    status = success ? 200 : 503;
                    break;
            }

            _db.Spans.Add(new StoredSpan
            {
                Id = Guid.NewGuid(), ProjectId = ProjectId, Release = release, Environment = "production",
                Platform = device.Platform, Operation = op, Name = name, Start = at,
                DurationMs = Math.Round(median * Math.Exp(NextGaussian() * 0.45), 1), // log-normal latencies
                StatusCode = status, Success = success, ReceivedAt = at,
            });
        }
    }

    private async Task TriageAsync(DateTime now, CancellationToken ct)
    {
        var issues = await _db.Issues.Where(i => i.ProjectId == ProjectId).ToListAsync(ct);

        foreach (var issue in issues)
        {
            switch (issue.Title)
            {
                case "NullReferenceException":
                    // Shipped in 2.4.0, fixed in 2.4.1.
                    issue.Status = IssueStatuses.Resolved;
                    issue.ResolvedAt = now.AddDays(-3);
                    issue.ResolvedInRelease = "2.4.1";
                    issue.AssignedTo = "ana@demo-shop.io";
                    break;
                case "HttpRequestException":
                    issue.AssignedTo = "luis@demo-shop.io";
                    break;
                case "InvalidOperationException":
                    // Was resolved before, came back: a regression.
                    issue.IsRegression = true;
                    issue.RegressedAt = now.AddDays(-5);
                    issue.AssignedTo = "ana@demo-shop.io";
                    break;
                case "MonoTouchException":
                    issue.Status = IssueStatuses.Ignored;
                    break;
            }
        }

        await _db.SaveChangesAsync(ct);
    }

    private async Task SeedFeedbackAsync(List<Guid> crashIds, DateTime now, CancellationToken ct)
    {
        string[] comments =
        [
            "I tapped Pay and the app just closed. Tried twice.",
            "Happened right after applying a discount code.",
            "Crashed when paying with the saved card.",
        ];

        for (var i = 0; i < crashIds.Count; i++)
        {
            var e = await _db.Events.AsNoTracking().FirstAsync(x => x.Id == crashIds[i], ct);
            var issueId = await _db.Issues.Where(x => x.ProjectId == e.ProjectId && x.Fingerprint == e.Fingerprint)
                .Select(x => (Guid?)x.Id).FirstOrDefaultAsync(ct);
            _db.Feedback.Add(new Data.Feedback
            {
                Id = Guid.NewGuid(), ProjectId = ProjectId, EventId = e.Id, IssueId = issueId,
                Comments = comments[i], UserId = e.UserId, CreatedAt = e.ReceivedAt.AddMinutes(3),
            });
        }

        await _db.SaveChangesAsync(ct);
    }

    private (string Release, DateTime At) RandomReleaseMoment(DateTime now)
    {
        var (release, fromDay, toDay) = Releases[_random.Next(Releases.Length)];
        var at = now.AddDays(-fromDay).AddMinutes(_random.NextDouble() * (fromDay - toDay) * 24 * 60);
        return (release, at > now ? now.AddMinutes(-_random.Next(1, 60)) : at);
    }

    private Problem Pick(Problem[] candidates, double totalWeight)
    {
        var roll = _random.NextDouble() * totalWeight;
        foreach (var p in candidates)
        {
            roll -= p.Weight;
            if (roll <= 0)
                return p;
        }
        return candidates[^1];
    }

    private (string Model, string Maker, string Os, string Platform) PickDevice(string platform)
    {
        var pool = platform == "any" ? Devices : Devices.Where(d => d.Platform == platform).ToArray();
        return pool[_random.Next(pool.Length)];
    }

    private List<Breadcrumb> Breadcrumbs(DateTime at)
    {
        var trail = new List<Breadcrumb>();
        var t = at.AddMinutes(-3);
        foreach (var page in Pages.OrderBy(_ => _random.Next()).Take(4))
        {
            t = t.AddSeconds(_random.Next(10, 40));
            trail.Add(new Breadcrumb { Timestamp = t, Category = BreadcrumbCategories.Navigation, Message = page });
        }
        trail.Add(new Breadcrumb
        {
            Timestamp = at.AddSeconds(-2), Category = BreadcrumbCategories.Http, Message = "POST api.demo-shop.io/payments",
            Data = { ["status_code"] = "200", ["duration_ms"] = _random.Next(300, 1400).ToString() }
        });
        return trail;
    }

    private double NextGaussian()
    {
        // Box–Muller.
        var u1 = 1.0 - _random.NextDouble();
        var u2 = _random.NextDouble();
        return Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
    }
}
