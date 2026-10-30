using Android.Accounts;
using Android.App;
using Android.Content;
using Android.OS;
using Jourfold.Mobile;

namespace Jourfold.Android;

/// <summary>
/// The account "Jourfold" that holds the trip calendars. It has no sign-in of its own; adding it in the system
/// settings opens the app, where the calendar is turned on.
/// </summary>
[Service(Name = "dev.jourfold.app.CalendarAccountService", Exported = true)]
[IntentFilter(["android.accounts.AccountAuthenticator"])]
[MetaData("android.accounts.AccountAuthenticator", Resource = "@xml/calendar_account")]
public sealed class CalendarAccountService : Service
{
    public override IBinder? OnBind(Intent? intent) => new Authenticator(this).IBinder;

    private sealed class Authenticator : AbstractAccountAuthenticator
    {
        private readonly Context context;
        public Authenticator(Context context) : base(context) => this.context = context;

        public override Bundle? AddAccount(AccountAuthenticatorResponse? response, string? accountType, string? authTokenType, string[]? requiredFeatures, Bundle? options)
        {
            var bundle = new Bundle();
            bundle.PutParcelable(AccountManager.KeyIntent, new Intent(context, typeof(MainActivity)));
            return bundle;
        }

        public override Bundle? ConfirmCredentials(AccountAuthenticatorResponse? response, Account? account, Bundle? options) => null;
        public override Bundle? EditProperties(AccountAuthenticatorResponse? response, string? accountType) => null;
        public override Bundle? GetAuthToken(AccountAuthenticatorResponse? response, Account? account, string? authTokenType, Bundle? options) => null;
        public override string? GetAuthTokenLabel(string? authTokenType) => null;
        public override Bundle? HasFeatures(AccountAuthenticatorResponse? response, Account? account, string[]? features)
        {
            var bundle = new Bundle(); bundle.PutBoolean(AccountManager.KeyBooleanResult, false); return bundle;
        }
        public override Bundle? UpdateCredentials(AccountAuthenticatorResponse? response, Account? account, string? authTokenType, Bundle? options) => null;
    }
}

/// <summary>
/// "Sync now" for the Jourfold account in the system settings, and a calendar app's refresh: takes newer versions of
/// the trips from GitHub only when that needs no decision, then writes the calendars. It never sends and never
/// combines, since nobody may be looking at the app. Syncs Android starts on its own, such as when the network comes
/// back, are not asked for by the user and do nothing.
/// </summary>
[Service(Name = "dev.jourfold.app.CalendarSyncService", Exported = true)]
[IntentFilter(["android.content.SyncAdapter"])]
[MetaData("android.content.SyncAdapter", Resource = "@xml/calendar_sync")]
public sealed class CalendarSyncService : Service
{
    private static Adapter? adapter;
    private static readonly Lock Gate = new();

    public override void OnCreate()
    {
        base.OnCreate();
        lock (Gate) adapter ??= new Adapter(ApplicationContext!);
    }

    public override IBinder? OnBind(Intent? intent) => adapter!.SyncAdapterBinder;

    private sealed class Adapter(Context context) : AbstractThreadedSyncAdapter(context, true)
    {
        public override void OnPerformSync(Account? account, Bundle? extras, string? authority, ContentProviderClient? provider, SyncResult? syncResult)
        {
            if (MobileApp.Host is not { } host || extras?.GetBoolean(ContentResolver.SyncExtrasManual) != true) return;
            try
            {
                var session = MobileApp.Session;
                var updated = SystemCalendars.RefreshAsync(host, session?.Workspace?.Repository.Root, MobileSession.KindColor).GetAwaiter().GetResult();
                // The trip list on screen shows the newer versions too.
                if (updated > 0 && session is not null) Avalonia.Threading.Dispatcher.UIThread.Post(() => _ = session.LoadTripsAsync());
            }
            catch (Exception) when (syncResult?.Stats is { } stats) { stats.NumIoExceptions++; }
        }
    }
}
