using System;
using System.IO;
using DXP;
using EDP;
using PCB;
using SCH;

namespace LcscLoader
{
    internal static class AdApi
    {
        private static IPCB_ServerInterface _pcbServer;
        private static ISch_ServerInterface _schServer;

        public static IClient Client => PluginContext.Client ?? DXP.GlobalVars.Client;

        public static IPCB_ServerInterface PcbServer
        {
            get
            {
                if (_pcbServer != null) return _pcbServer;
                Client.StartServer("PCB");
                _pcbServer = Client.GetServerModuleByName("PCB") as IPCB_ServerInterface;
                if (_pcbServer == null) throw new InvalidOperationException("无法启动 PCB 服务器");
                return _pcbServer;
            }
        }

        public static ISch_ServerInterface SchServer
        {
            get
            {
                if (_schServer != null) return _schServer;
                Client.StartServer("SCH");
                _schServer = Client.GetServerModuleByName("SCH") as ISch_ServerInterface;
                if (_schServer == null) throw new InvalidOperationException("无法启动 SCH 服务器");
                return _schServer;
            }
        }

        public static int MmToCoord(double mm) => EDP.Utils.MMsToCoord(mm);

        public static int MilsToCoord(double mils) => EDP.Utils.MilsToCoord(mils);

        public static string LibraryDir
        {
            get
            {
                var directory = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "AltiumLCSC");
                Directory.CreateDirectory(directory);
                return directory;
            }
        }

        public static string SchLibPath => Path.Combine(LibraryDir, "LCSC.SchLib");

        public static string PcbLibPath => Path.Combine(LibraryDir, "LCSC.PcbLib");

        public static string ModelDir
        {
            get
            {
                var directory = Path.Combine(LibraryDir, "models");
                Directory.CreateDirectory(directory);
                return directory;
            }
        }

        public static bool SymbolExists(string libReference, string libraryPath)
        {
            try
            {
                return SchServer.LoadComponentFromLibrary(libReference, libraryPath) != null;
            }
            catch
            {
                return false;
            }
        }

        // Starts Altium's interactive placement (ghost follows the cursor).
        public static bool PlaceFromLibrary(string libReference, string libraryPath)
        {
            try
            {
                var view = Client.GetCurrentView();
                var document = view == null ? null : view.GetOwnerDocument();
                PluginLog.Write("place doc=" + (document == null ? "null" : document.GetKind()));
                if (document != null) document.Focus();
                try
                {
                    var setFocus = view == null ? null : view.GetType().GetMethod("SetFocus");
                    setFocus?.Invoke(view, null);
                }
                catch
                {
                    // Focus is best-effort.
                }

                var launcher = PluginContext.Launcher;
                if (launcher != null)
                {
                    var parameters = "LibReference=" + libReference + " |Library=" + libraryPath;
                    launcher.LaunchCommand("IntegratedLibrary:PlaceLibraryComponent",
                        ref parameters, null);
                    PluginLog.Write("place via command");
                    return true;
                }

                Client.StartServer("IntegratedLibrary");
                var manager = Client.GetServerModuleByName("IntegratedLibrary")
                    as SCH.IIntegratedLibraryManager;
                if (manager == null)
                {
                    ImportLog.Add("交互式放置失败：命令启动器与管理器都不可用");
                    return false;
                }

                var placed = manager.PlaceLibraryComponent(libReference, libraryPath, "");
                PluginLog.Write("place via api=" + placed);
                if (!placed) ImportLog.Add("交互式放置失败：库中未找到 " + libReference);
                return placed;
            }
            catch (Exception ex)
            {
                ImportLog.Add("交互式放置失败：" + ex.Message);
                return false;
            }
        }

        // Brings an already-open schematic to the front when the active view
        // is not a schematic sheet (for example the Home Page).
        private static ISch_Document EnsureSchDocumentOpen()
        {
            var doc = SchServer.GetCurrentSchDocument();
            if (doc != null && doc.GetState_ObjectId() != SCH.TObjectId.eSchLib) return doc;

            try
            {
                var module = SchServer as DXP.IServerModule;
                if (module != null)
                {
                    var count = module.GetDocumentCount();
                    for (var i = 0; i < count; i++)
                    {
                        var serverDoc = module.Internal_GetDocuments(i) as IServerDocument;
                        if (serverDoc == null) continue;
                        var kind = serverDoc.GetKind();
                        PluginLog.Write("open doc " + i + " kind=" + kind + " file=" + serverDoc.GetFileName());
                        if (!string.Equals(kind, "SCH", StringComparison.OrdinalIgnoreCase)) continue;
                        try { Client.ShowDocument(serverDoc); } catch { }
                        try { serverDoc.Focus(); } catch { }
                        doc = SchServer.GetCurrentSchDocument();
                        if (doc != null && doc.GetState_ObjectId() != SCH.TObjectId.eSchLib)
                        {
                            PluginLog.Write("switched to schematic " + serverDoc.GetFileName());
                            return doc;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                PluginLog.Write("ensure sch document error: " + ex.Message);
            }
            return null;
        }

        // Routes AD's own library placement process through the process
        // launcher, the same path Altium scripts use for RunProcess.
        public static bool PlaceViaProcess(string libReference, string libraryPath)
        {
            try
            {
                var doc = EnsureSchDocumentOpen();
                if (doc == null) { ImportLog.Add("请先打开原理图"); return false; }
                if (doc.GetState_ObjectId() == SCH.TObjectId.eSchLib)
                {
                    ImportLog.Add("当前是原理图库，不能放置");
                    return false;
                }

                var view = Client.GetCurrentView();
                FocusViewDocument(view);
                var launcher = ResolveProcessLauncher(view);
                PluginLog.Write("process launcher=" + (launcher == null ? "null" : launcher.GetType().FullName));
                if (launcher == null) return false;

                var parameters = "LibReference=" + libReference + "|Library=" + libraryPath;
                var started = DateTime.UtcNow;
                launcher.SendMessage("IntegratedLibrary:PlaceLibraryComponent", ref parameters, view);
                var elapsed = (int)(DateTime.UtcNow - started).TotalMilliseconds;
                PluginLog.Write("process place elapsed=" + elapsed);
                if (elapsed < 150)
                {
                    PluginLog.Write("process place returned immediately");
                    return false;
                }
                ImportLog.Add("已放置到原理图：" + libReference);
                return true;
            }
            catch (Exception ex)
            {
                PluginLog.Write("process place exception: " + ex);
                return false;
            }
        }

        private static IProcessLauncher ResolveProcessLauncher(IServerDocumentView view)
        {
            try { var launcher = Client as IProcessLauncher; if (launcher != null) return launcher; } catch { }
            try { var launcher = view as IProcessLauncher; if (launcher != null) return launcher; } catch { }
            try { var launcher = PluginContext.Launcher as IProcessLauncher; if (launcher != null) return launcher; } catch { }
            return null;
        }

        // Adds the component and lets Altium's own location chooser pick the
        // final point, so the user clicks where the part should land.
        public static bool PlaceWithLocationPicker(string libReference, string libraryPath)
        {
            try
            {
                var doc = EnsureSchDocumentOpen();
                if (doc == null) { ImportLog.Add("请先打开原理图"); return false; }
                if (doc.GetState_ObjectId() == SCH.TObjectId.eSchLib)
                {
                    ImportLog.Add("当前是原理图库，不能放置");
                    return false;
                }

                var component = SchServer.LoadComponentFromLibrary(libReference, libraryPath)
                    as ISch_Component;
                if (component == null) { ImportLog.Add("库中没有该器件：" + libReference); return false; }

                doc.AddSchObject(component);
                component.SetState_Orientation(TRotationBy90.eRotate0);
                var rect = doc.Internal_Graphical_VirtualRectangle();
                var centerX = (rect.GetLeft() + rect.GetRight()) / 2;
                var centerY = (rect.GetTop() + rect.GetBottom()) / 2;
                component.MoveToXY(centerX, centerY);

                var graphical = component as ISch_GraphicalObject;
                if (graphical != null) graphical.SetState_Selection(true);
                doc.GraphicallyInvalidate();

                DXP.IPoint point = new DXP.Point { X = centerX, Y = centerY };
                var timer = new System.Windows.Forms.Timer { Interval = 30 };
                timer.Tick += (sender, args) =>
                {
                    try
                    {
                        var x = point.GetX();
                        var y = point.GetY();
                        if (x == centerX && y == centerY) return;
                        component.MoveToXY(x, y);
                        doc.GraphicallyInvalidate();
                    }
                    catch (Exception ex)
                    {
                        PluginLog.Write("picker tick error: " + ex.Message);
                        timer.Stop();
                    }
                };
                timer.Start();
                var ok = false;
                try { ok = doc.ChooseLocationInteractively(ref point, "放置 " + libReference); }
                finally
                {
                    timer.Stop();
                    timer.Dispose();
                }

                PluginLog.Write("choose location ok=" + ok + " x=" + point.GetX() + " y=" + point.GetY());
                if (ok)
                {
                    component.MoveToXY(point.GetX(), point.GetY());
                    doc.GraphicallyInvalidate();
                    ImportLog.Add("已放置到原理图：" + libReference);
                }
                else
                {
                    ImportLog.Add("已取消放置，器件保留在图纸中间");
                }
                return true;
            }
            catch (Exception ex)
            {
                ImportLog.Add("放置失败：" + ex.Message);
                PluginLog.Write("picker place exception: " + ex);
                return false;
            }
        }

        // Drops the component on the sheet and immediately starts Altium's
        // move-selection mode so it follows the cursor until the user clicks.
        public static bool PlaceInteractive(string libReference, string libraryPath)
        {
            try
            {
                var doc = EnsureSchDocumentOpen();
                if (doc == null)
                {
                    ImportLog.Add("请先打开原理图");
                    return false;
                }
                if (doc.GetState_ObjectId() == SCH.TObjectId.eSchLib)
                {
                    ImportLog.Add("当前是原理图库，不能放置");
                    return false;
                }

                var component = SchServer.LoadComponentFromLibrary(libReference, libraryPath)
                    as ISch_Component;
                if (component == null)
                {
                    ImportLog.Add("库中没有该器件：" + libReference);
                    return false;
                }

                var view = Client.GetCurrentView();
                PluginLog.Write("place interactive view=" + (view == null ? "null" : view.GetType().Name));
                FocusViewDocument(view);
                var launcher = PluginContext.Launcher;
                if (launcher != null)
                {
                    var deselect = "Action=All";
                    try { launcher.LaunchCommand("Sch:DeSelect", ref deselect, view); } catch { }
                }

                var rect = doc.Internal_Graphical_VirtualRectangle();
                var centerX = (rect.GetLeft() + rect.GetRight()) / 2;
                var centerY = (rect.GetTop() + rect.GetBottom()) / 2;

                doc.AddSchObject(component);
                component.MoveToXY(centerX, centerY);
                component.SetState_Orientation(TRotationBy90.eRotate0);

                var graphical = component as ISch_GraphicalObject;
                PluginLog.Write("place selection cast=" + (graphical != null));
                if (graphical != null)
                {
                    graphical.SetState_Selection(true);
                    PluginLog.Write("place selected=" + graphical.GetState_Selection());
                }
                doc.GraphicallyInvalidate();

                if (launcher == null)
                {
                    ImportLog.Add("已放置到原理图（未进入移动状态）");
                    return true;
                }

                var move = "";
                launcher.LaunchCommand("Sch:MoveSelectedObjects", ref move, view);
                PluginLog.Write("move selection command sent (view context)");
                ImportLog.Add("已进入放置状态，请移动鼠标并点击放置");
                return true;
            }
            catch (Exception ex)
            {
                ImportLog.Add("交互式放置失败：" + ex.Message);
                return false;
            }
        }

        private static void FocusViewDocument(IServerDocumentView view)
        {
            try
            {
                var document = view == null ? null : view.GetOwnerDocument();
                document?.Focus();
            }
            catch
            {
                // Focus is best-effort.
            }
        }

        public static IServerDocument OpenDocument(string kind, string path)
        {
            return Client.OpenDocument(kind, path);
        }
    }
}
