using System.Text.Json;

namespace HoverDeck;

internal static class SelfTests
{
    public static void Run(string output)
    {
        var passed = new List<string>();
        void Check(string label, bool result) { if (!result) throw new Exception("FAIL: " + label); passed.Add("PASS: " + label); }
        try
        {
            const string a = "C:\\Games\\VersionA\\Wow.exe", b = "C:\\Games\\VersionB\\Wow.exe";
            var l = new Layout { Executable = a, Slots = new() { new() { Id = "a", X = -1920, Y = 0, Width = 960, Height = 1080 }, new() { Id = "b", X = 0, Y = 0, Width = 1920, Height = 1080 }, new() { Id = "c", Executable = b, X = 0, Y = 1080 } } };
            var w1 = new GameWindow(1, 101, 1000, a, "WoW", l.Slots[0].Bounds, false);
            var w2 = new GameWindow(2, 102, 1001, a, "WoW", l.Slots[1].Bounds, false);
            var wrong = new GameWindow(3, 103, 1002, b, "WoW", l.Slots[1].Bounds, false);
            var result = Matching.Assign(l, new[] { w2, wrong, w1 }, new Dictionary<string, string>());
            Check("Identical executable names from different folders remain separate", result["c"].ProcessId == 103 && result["b"].ProcessId == 102);
            Check("Existing positions map to corresponding slots", result["a"].ProcessId == 101);
            Check("Each process fills at most one slot", result.Values.Select(w => w.ProcessId).Distinct().Count() == 3);
            result = Matching.Assign(l, new[] { w1 }, new Dictionary<string, string>());
            Check("Only missing slots require launching", result.Count == 1 && result.ContainsKey("a"));
            result = Matching.Assign(l, new[] { w1, w2 }, new Dictionary<string, string> { ["a"] = w2.Identity });
            Check("Live bindings survive manual window moves", result["a"].ProcessId == 102 && result["b"].ProcessId == 101);
            result = Matching.Assign(l, new[] { w1 }, new Dictionary<string, string> { ["b"] = "101:999" });
            Check("Reused process IDs do not revive stale bindings", result.ContainsKey("a") && !result.ContainsKey("b"));
            result = Matching.Assign(l, new[] { w1, w1 }, new Dictionary<string, string>());
            Check("Multiple HWNDs from one process are deduplicated", result.Count == 1);
            Check("Path matching ignores Windows path case", Matching.SamePath(a, a.ToUpperInvariant()));
            Check("Empty executable paths never match", !Matching.SamePath("", ""));
            var monitors = new[] { new MonitorInfo("DISPLAY2", false, new(-1920, 0, 1920, 1080), new(-1920, 0, 1920, 1040)), new MonitorInfo("DISPLAY1", true, new(0, 0, 2560, 1440), new(0, 0, 2560, 1400)) };
            Check("Negative-coordinate display layouts are valid", Matching.OnScreen(l.Slots[0].Bounds, monitors));
            Check("Disconnected display placements are rejected", !Matching.OnScreen(new(9000, 9000, 960, 540), monitors));
            Check("Only a tiny offscreen sliver is insufficient", !Matching.OnScreen(new(2550, 0, 960, 540), monitors));
            var config = new Settings { DelayMs = 0, Layouts = new() { l } };
            var roundtrip = JsonSerializer.Deserialize<Settings>(JsonSerializer.Serialize(config, Storage.Json), Storage.Json)!;
            Check("Profiles round-trip paths, negative coordinates and zero delay", roundtrip.DelayMs == 0 && roundtrip.Layouts[0].Slots[0].X == -1920 && roundtrip.Layouts[0].Slots[2].Executable == b);
            Check("Older profiles default to direct launch", JsonSerializer.Deserialize<Slot>("{\"Name\":\"old\"}")!.LaunchMethod == ClientLaunchMethod.Direct);
            var licenses = new[] { "WoW1 (US)", "WoW1 (EU)", "WoW3 (EU)", "WoW4 (EU) - Starter Edition", "WoW9 (KR)", "WoW10 (EU)" };
            var bn = new BattleNetLaunch { AccountNumber = 3, Region = "Auto", GameTabId = "game-nav-btn-WoWC", GameVersion = "World of Warcraft Classic", Executable = a };
            Check("Generic WoW number resolves the actual launcher region", BattleNetAccounts.Resolve(bn, licenses) == "WoW3 (EU)");
            bn.AccountNumber = 1; bool ambiguous = false; try { BattleNetAccounts.Resolve(bn, licenses); } catch (InvalidOperationException) { ambiguous = true; }
            Check("Automatic region refuses ambiguous account numbers", ambiguous);
            bn.Region = "EU"; Check("Explicit region chooses the exact WoW license", BattleNetAccounts.Resolve(bn, licenses) == "WoW1 (EU)");
            bn.AccountNumber = 4; Check("Starter suffix does not change account identity", BattleNetAccounts.Resolve(bn, licenses) == "WoW4 (EU) - Starter Edition");
            bn.AccountNumber = 9; bn.Region = "Auto"; Check("WoW1–WoW9 selection is not tied to the developer's account list", BattleNetAccounts.Resolve(bn, licenses) == "WoW9 (KR)");
            bn.AccountNumber = 1; bn.Region = "KR"; bool absent = false; try { BattleNetAccounts.Resolve(bn, licenses); } catch (InvalidOperationException) { absent = true; }
            Check("Missing license/region fails without using a different account", absent);
            var named = new BattleNetLaunch { AccountName = "OriginalLicense (EU)" };
            Check("Refreshed names work without a fixed WoW-number list", BattleNetAccounts.Resolve(named, new[] { "OriginalLicense (EU)", "OriginalLicense (US)" }) == "OriginalLicense (EU)");
            bool changedAccount = false; try { BattleNetAccounts.Resolve(named, new[] { "OriginalLicense (US)" }); } catch (InvalidOperationException) { changedAccount = true; }
            Check("A refreshed account never falls back to another region", changedAccount);
            var catalog = new BattleNetCatalog();
            var firstChoices = new BattleNetChoices("game-nav-btn-WoWC", "World of Warcraft Classic", "WoW1 (EU)", new() { "World of Warcraft Classic", "World of Warcraft" }, new() { "WoW1 (EU)", "WoW2 (US)" });
            catalog.Remember(firstChoices);
            LibraryTests.Run(Check, catalog, firstChoices);
            ClientPlacementTests.Run(Check);
            TaskbarCoverageTests.Run(Check);
            WindowStackingTests.Run(Check);
            var bnSlot = new Slot { Id = "bn", LaunchMethod = ClientLaunchMethod.BattleNet, BattleNet = bn };
            var bnLayout = new Layout { Slots = new() { bnSlot } };
            Check("Battle.net never guesses an account from window position", Matching.Assign(bnLayout, new[] { w1 }, new Dictionary<string, string>()).Count == 0);
            bn.BoundIdentity = w1.Identity;
            var bnCopy = JsonSerializer.Deserialize<Layout>(JsonSerializer.Serialize(bnLayout, Storage.Json), Storage.Json)!;
            Check("Battle.net assignment survives application restart", Matching.Assign(bnCopy, new[] { w1 }, new Dictionary<string, string>())["bn"].Identity == w1.Identity);
            Check("Reused PID cannot inherit a Battle.net account", Matching.Assign(bnCopy, new[] { w1 with { Started = 1008 } }, new Dictionary<string, string>()).Count == 0);
            bn.ClearBinding(); Check("Changing account removes both binding and pending launch", bn.BoundIdentity == "" && bn.PendingSinceUtc == "" && bn.BeforeLaunch.Count == 0);
            var left = new MonitorInfo("LEFT", false, new(-1440, -1114, 1440, 2560), new(-1440, -1114, 1440, 2512));
            var primary = new MonitorInfo("PRIMARY", true, new(0, 0, 2560, 1440), new(0, 0, 2560, 1392));
            var snapshot = new List<MonitorInfo> { left, primary };
            var liveDesktop = new DesktopState(() => snapshot);
            var leftBar = DesktopGeometry.ReservedAreas(liveDesktop.Monitors[0]).Single();
            var primaryBar = DesktopGeometry.ReservedAreas(liveDesktop.Monitors[1]).Single();
            Check("Per-monitor taskbar retains the left display's six-pixel vertical offset", leftBar.Bounds.Top == 1398 && primaryBar.Bounds.Top == 1392 && leftBar.Bounds.Bottom == 1446);
            snapshot = new() { left with { Bounds = new(-1440, -714, 1440, 2560), WorkArea = new(-1440, -714, 1440, 2512) }, primary };
            Check("Live desktop detects a monitor move with unchanged resolution", liveDesktop.Refresh());
            Check("Moved display's taskbar follows its new origin by 400 pixels", DesktopGeometry.ReservedAreas(liveDesktop.Monitors[0]).Single().Bounds.Top == 1798);
            Check("Unmoved display's taskbar remains at its own position", DesktopGeometry.ReservedAreas(liveDesktop.Monitors[1]).Single().Bounds.Top == 1392);
            Check("Unchanged snapshots do not trigger unnecessary layout refreshes", !liveDesktop.Refresh());
            var autoHidden = primary with { WorkArea = primary.Bounds };
            snapshot = new() { left, autoHidden };
            Check("Work-area-only changes trigger refresh", liveDesktop.Refresh());
            Check("No reserved taskbar space is invented when Windows reserves none", !DesktopGeometry.ReservedAreas(autoHidden).Any());
            var sideBar = primary with { WorkArea = new(64, 0, 2496, 1440) };
            var side = DesktopGeometry.ReservedAreas(sideBar).Single();
            Check("A left-side taskbar uses Windows' actual edge and width", side.Edge == "left" && side.Bounds == new Rectangle(0, 0, 64, 1440));
            var overflow = DesktopGeometry.OffscreenParts(new(-1440, 0, 1440, 1483), new[] { left, primary });
            Check("Client overflow does not redefine the monitor boundary", overflow.Count == 1 && overflow[0] == new Rectangle(-1440, 1446, 1440, 37));
            snapshot = new() { primary };
            Check("Disconnecting a monitor updates the live snapshot", liveDesktop.Refresh() && liveDesktop.Monitors.Count == 1);
            // Regression: v1.0.2 wrongly cropped the user's full client rectangle
            // to the monitor's work area, hiding its saved bottom edge/resize grip.
            var fullSlot = new Slot { X = -1440, Y = 0, Width = 1440, Height = 1483 };
            using var canvas = new LayoutCanvas { Size = new(1000, 650), Monitors = new() { left, primary }, Layout = new Layout { Slots = new() { fullSlot } }, Selected = 0 };
            using var bitmap = new Bitmap(canvas.Width, canvas.Height);
            canvas.DrawToBitmap(bitmap, canvas.ClientRectangle);
            var projected = canvas.Project(fullSlot.Bounds);
            var handlePixel = bitmap.GetPixel((int)(projected.Right - 3), (int)(projected.Bottom - 3));
            Check("Rendering preserves the complete client rectangle and resize grip below the display", handlePixel.ToArgb() == LayoutCanvas.SlotColors[0].ToArgb());
            Check("Rendering does not change the user's saved client coordinates", fullSlot.Bounds == new Rectangle(-1440, 0, 1440, 1483));
            var outerWindow = new Rectangle(0, 0, 2560, 1392);
            var visibleWindow = new Rectangle(7, 0, 2546, 1385);
            Check("Fit screen compensates for the measured invisible WoW borders", WindowGeometry.OuterForVisible(outerWindow, outerWindow, visibleWindow) == new Rectangle(-7, 0, 2574, 1399));
            Check("Negative-monitor placement preserves the intended visible edges", WindowGeometry.OuterForVisible(new(-1440, 286, 1440, 1483), outerWindow, visibleWindow) == new Rectangle(-1447, 286, 1454, 1490));
            Check("Borderless windows are not enlarged", WindowGeometry.OuterForVisible(outerWindow, outerWindow, outerWindow) == outerWindow);
            Check("Different DPI and asymmetric frames use their measured margins", WindowGeometry.OuterForVisible(new(100, 200, 800, 600), new(10, 20, 1000, 900), new(21, 25, 978, 882)) == new Rectangle(89, 195, 822, 618));
            Check("Invalid DWM rectangles cannot create an oversized placement", !WindowGeometry.IsUsableFrame(outerWindow, Rectangle.Empty) && !WindowGeometry.IsUsableFrame(outerWindow, new(-32000, -32000, 160, 160)));
            var testCredentials = new LoginCredentials("test-account-" + Guid.NewGuid().ToString("N"), "test-password-" + Guid.NewGuid().ToString("N"));
            string protectedLogin = CredentialProtection.Protect(testCredentials);
            Check("Windows user encryption round-trips both account and password", CredentialProtection.Unprotect(protectedLogin) == testCredentials);
            var loginSlot = new Slot { Login = new ClientLogin { ProtectedCredentials = protectedLogin, Executable = a } };
            string serializedLogin = JsonSerializer.Serialize(loginSlot, Storage.Json);
            Check("Saved layouts contain no plaintext account or password", !serializedLogin.Contains(testCredentials.Account) && !serializedLogin.Contains(testCredentials.Password));
            bool rejectedCredential = false; try { CredentialProtection.Unprotect("invalid-encrypted-data"); } catch (InvalidOperationException ex) { rejectedCredential = !ex.Message.Contains(testCredentials.Password); }
            Check("Unreadable saved credentials fail without exposing secrets", rejectedCredential);
            Check("Automatic login excludes existing clients and disabled profiles", !ClientLoginService.ShouldLogin(loginSlot.Login, false) && !ClientLoginService.ShouldLogin(new() { OnLaunch = false }, true) && ClientLoginService.ShouldLogin(loginSlot.Login, true));
            Check("Login input uses the correct native INPUT structure layout", System.Runtime.InteropServices.Marshal.SizeOf<LoginInput.Input>() == (IntPtr.Size == 8 ? 40 : 28));
            var unicodeEvents = LoginInput.TextEvents("a+{å}");
            Check("Login text treats symbols and Unicode as literal characters", unicodeEvents.Skip(6).Where((_, i) => i % 2 == 0).Select(e => (char)e.Data.Keyboard.Scan).SequenceEqual("a+{å}"));
            using var loginBitmap = new Bitmap(800, 600);
            using (var graphics = Graphics.FromImage(loginBitmap)) { graphics.Clear(Color.Black); for (int i = 0; i < 10; i++) graphics.FillRectangle(i % 2 == 0 ? Brushes.White : Brushes.Blue, 20 + i * 9, 20, 6, 32); }
            using var marker = loginBitmap.Clone(new Rectangle(20, 20, 96, 32), System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using var markerStream = new MemoryStream(); marker.Save(markerStream, System.Drawing.Imaging.ImageFormat.Png);
            var loginScreen = new LoginScreen { Width = 800, Height = 600, Account = new(200, 200), Password = new(200, 250), Submit = new(200, 300), Landmark = new(20, 20, 96, 32), LandmarkPng = Convert.ToBase64String(markerStream.ToArray()) };
            Check("Configured login marker is recognized", LoginScreenMatch.Matches(loginBitmap, loginScreen));
            using var wrongScreen = new Bitmap(800, 600); using var wrongSize = new Bitmap(801, 600);
            Check("Different screens and resized clients fail login recognition", !LoginScreenMatch.Matches(wrongScreen, loginScreen) && !LoginScreenMatch.Matches(wrongSize, loginScreen));
            Check("Blank markers cannot authorize password entry", !LoginScreenMatch.HasDetail(wrongScreen));
            loginSlot.Login!.Screen = loginScreen;
            var copiedLogin = JsonSerializer.Deserialize<Slot>(JsonSerializer.Serialize(loginSlot, Storage.Json), Storage.Json)!;
            Check("Each saved client retains its login fields and independently editable settings", copiedLogin.Login!.Screen!.Account == loginScreen.Account && copiedLogin.Login.Screen.IsValid && !ReferenceEquals(copiedLogin.Login, loginSlot.Login));
            Check("Existing saved login setups retain explicit field positions", !copiedLogin.Login.Screen.KeyboardNavigation && copiedLogin.Login.Screen.Password == loginScreen.Password && copiedLogin.Login.Screen.Submit == loginScreen.Submit);
            loginScreen.KeyboardNavigation = true; loginScreen.Password = loginScreen.Submit = default;
            Check("Keyboard setup needs only the account field and screen marker", loginScreen.IsValid && LoginScreenMatch.Matches(loginBitmap, loginScreen));
            var simpleCopy = JsonSerializer.Deserialize<LoginScreen>(JsonSerializer.Serialize(loginScreen, Storage.Json), Storage.Json)!;
            Check("Keyboard navigation choice survives saving and reopening", simpleCopy.KeyboardNavigation && simpleCopy.IsValid);
            simpleCopy.LandmarkPng = "";
            Check("Keyboard navigation still requires login-screen recognition", !simpleCopy.IsValid);
            Check("Native DEVMODEW layout has the required size and position offsets", System.Runtime.InteropServices.Marshal.SizeOf<WindowsDisplaySettings.DevMode>() == 220 && System.Runtime.InteropServices.Marshal.OffsetOf<WindowsDisplaySettings.DevMode>("X").ToInt32() == 76);
            var arrangement = new List<MonitorInfo> { left, primary };
            Check("Current extended desktop passes arrangement validation", DisplayArrangement.Validate(arrangement) is null);
            var moved = DisplayArrangement.Move(left, new(-1440, -800));
            Check("Moving a monitor carries its taskbar work area by the same offset", moved.Bounds.Y == -800 && moved.WorkArea.Y == -800 && moved.Bounds.Bottom - moved.WorkArea.Bottom == 48);
            Check("Draft monitor edits do not mutate the live desktop snapshot", left.Bounds.Y == -1114 && arrangement[0].Bounds.Y == -1114);
            using var fixedView = new LayoutCanvas { Size = new(1000, 650), Monitors = new() { left, primary }, DisplayEditMode = true };
            var leftBeforeEdit = fixedView.Project(left.Bounds); var primaryBeforeEdit = fixedView.Project(primary.Bounds);
            fixedView.Monitors[0] = DisplayArrangement.Move(left, new(-4000, -2100));
            var leftAfterEdit = fixedView.Project(fixedView.Monitors[0].Bounds);
            Check("Editing monitor X/Y preserves every display's drawn size", leftAfterEdit.Size == leftBeforeEdit.Size && fixedView.Project(primary.Bounds).Size == primaryBeforeEdit.Size);
            Check("Editing monitor X/Y does not shift unedited displays", fixedView.Project(primary.Bounds) == primaryBeforeEdit);
            double editScale = leftBeforeEdit.Width / left.Bounds.Width;
            Check("Only the edited display moves by its coordinate delta at the fixed scale", Math.Abs(leftAfterEdit.X - leftBeforeEdit.X - (-4000 - left.Bounds.X) * editScale) < .001 && Math.Abs(leftAfterEdit.Y - leftBeforeEdit.Y - (-2100 - left.Bounds.Y) * editScale) < .001);
            var draftBeforeFit = fixedView.Monitors.ToList(); fixedView.FitView();
            Check("Explicit Fit view refits the preview without changing monitor coordinates", fixedView.Project(primary.Bounds).Size != primaryBeforeEdit.Size && DisplayArrangement.SamePositions(draftBeforeFit, fixedView.Monitors));
            var newPrimary = DisplayArrangement.Normalize(new[] { left with { Primary = true }, primary with { Primary = false } });
            Check("Changing primary normalizes coordinates without changing relative monitor positions", newPrimary[0].Bounds.Location == Point.Empty && newPrimary[1].Bounds.Location == new Point(1440, 1114));
            Check("Overlapping monitor arrangements are rejected", DisplayArrangement.Validate(new[] { primary, left with { Bounds = new(0, 0, 1440, 2560) } }) is not null);
            Check("Disconnected monitor islands are rejected", DisplayArrangement.Validate(new[] { primary, DisplayArrangement.Move(left, new(-3000, -1114)) }) is not null);
            Check("Corner-only contact is rejected because no traversable edge exists", DisplayArrangement.Validate(new[] { primary, DisplayArrangement.Move(left, new(-1440, 1440)) }) is not null);
            Check("Display snapping joins neighboring edges exactly", DisplayArrangement.Snap(new(-1430, -1114, 1440, 2560), new[] { primary.Bounds }, 20).Right == 0);
            var stub = new TestDisplayBackend { FailApply = true }; var backup = stub.Capture();
            try { DisplayChangeSession.ApplyWithRollback(stub, arrangement, backup); } catch (InvalidOperationException) { }
            Check("A partial Windows apply failure triggers restoration of the original backup", stub.ApplyCalls == 1 && stub.RestoreCalls == 1 && ReferenceEquals(stub.Restored, backup));
            stub = new TestDisplayBackend(); DisplayChangeSession.ApplyWithRollback(stub, arrangement, stub.Capture());
            Check("Successful apply is left pending for user confirmation", stub.ApplyCalls == 1 && stub.RestoreCalls == 0);
            using (var timeout = new DisplayKeepForm(DateTimeOffset.UtcNow.AddMilliseconds(250), new Rectangle(0, 0, 1920, 1080)))
                Check("Unconfirmed display changes time out to Revert", timeout.ShowDialog() == DialogResult.Cancel);
            File.WriteAllLines(output, passed.Append($"{passed.Count} tests passed."));
        }
        catch (Exception ex) { File.WriteAllLines(output, passed.Append(ex.ToString())); Environment.ExitCode = 1; }
    }
    private sealed class TestDisplayBackend : IDisplaySettingsBackend
    {
        public bool FailApply;
        public int ApplyCalls, RestoreCalls;
        public DisplayBackup? Restored;
        public DisplayBackup Capture() => new() { Current = new() { new("TEST", 0, 0, true) }, Registered = new() { new("TEST", 0, 0, true) } };
        public void Test(IReadOnlyList<MonitorInfo> plan) { }
        public void Apply(IReadOnlyList<MonitorInfo> plan) { ApplyCalls++; if (FailApply) throw new InvalidOperationException("Simulated driver failure after staging."); }
        public void Restore(DisplayBackup backup) { RestoreCalls++; Restored = backup; }
    }
}

