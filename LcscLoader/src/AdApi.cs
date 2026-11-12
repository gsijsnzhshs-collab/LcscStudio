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

        // 官方示例使用 SchServer.ProcessControl.PreProcess(SchDoc, "")。
        // Client 的 ProcessControl 不属于 SCH 文档，传入 SchLib 会抛外部组件异常。
        public static IProcessControl ProcessControl
        {
            get
            {
                try
                {
                    var module = SchServer as IServerModule;
                    return module == null ? null : module.GetProcessControl();
                }
                catch (Exception ex)
                {
                    PluginLog.Write("get sch process control failed: " + ex.Message);
                    return null;
                }
            }
        }

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

        // 原理图文档对象：优先用当前视图的 owner document —— 把 ISch_Document
        // 直接强转成 IServerDocument 会拿到不可用的代理（调用 GetFileName 抛异常）。
        private static IServerDocument FindSchServerDocument()
        {
            try
            {
                var view = Client.GetCurrentView();
                var owner = view == null ? null : view.GetOwnerDocument();
                if (owner != null && IsSchDoc(owner.GetFileName())) return owner;
            }
            catch (Exception ex)
            {
                PluginLog.Write("find sch view error: " + ex.Message);
            }

            try
            {
                var last = Client.Internal_LastActiveDocumentOfType("SCH") as IServerDocument;
                if (last != null && IsSchDoc(last.GetFileName())) return last;
            }
            catch
            {
                // Fall through to the module scan.
            }

            try
            {
                var module = SchServer as DXP.IServerModule;
                if (module != null)
                {
                    var count = module.GetDocumentCount();
                    for (var i = 0; i < count; i++)
                    {
                        var serverDoc = module.Internal_GetDocuments(i) as IServerDocument;
                        if (serverDoc != null && IsSchDoc(serverDoc.GetFileName())) return serverDoc;
                    }
                }
            }
            catch (Exception ex)
            {
                PluginLog.Write("find sch document error: " + ex.Message);
            }
            return null;
        }

        private static bool IsSchDoc(string path)
        {
            return string.Equals(Path.GetExtension(path), ".SchDoc", StringComparison.OrdinalIgnoreCase);
        }

        private static ISch_Document ActivateSchDocument(IServerDocument serverDoc)
        {
            try
            {
                var shown = false;
                try { shown = serverDoc.GetIsShown(); } catch { }
                Client.ShowDocument(serverDoc);
                try { serverDoc.Focus(); } catch { }
                PluginLog.Write("activated schematic shown=" + shown + " file=" + serverDoc.GetFileName());
            }
            catch (Exception ex)
            {
                PluginLog.Write("activate schematic error: " + ex.Message);
            }

            var doc = SchServer.GetCurrentSchDocument();
            if (doc != null && doc.GetState_ObjectId() != SCH.TObjectId.eSchLib) return doc;
            return null;
        }

        private static IServerDocumentView ResolveSchView(IServerDocument serverDoc)
        {
            var current = Client.GetCurrentView();
            if (current != null)
            {
                var owner = current.GetOwnerDocument();
                if (owner != null && SameDocument(owner, serverDoc)) return current;
            }

            for (var index = 0; index <= 1; index++)
            {
                try
                {
                    var candidate = serverDoc.Internal_GetView(index) as IServerDocumentView;
                    if (candidate != null)
                    {
                        PluginLog.Write("resolve view index=" + index);
                        return candidate;
                    }
                }
                catch
                {
                    // Index not present; try the other one.
                }
            }

            if (current != null)
            {
                var owner = current.GetOwnerDocument();
                PluginLog.Write("resolve view fallback owner=" +
                                (owner == null ? "null" : owner.GetKind() + ":" + owner.GetFileName()));
            }
            return current;
        }

        private static bool SameDocument(IServerDocument a, IServerDocument b)
        {
            try
            {
                return string.Equals(a.GetFileName(), b.GetFileName(), StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        // 符号库没有在 AD 里打开时，按路径查找器件的命令会 0ms 空返回；
        // 放置前先把库文档以隐藏方式打开，避免切走用户正在看的原理图。
        private static void EnsureLibraryOpen(string libraryPath)
        {
            try
            {
                var doc = EnsureDocumentHidden("SchLib", libraryPath);
                var shown = false;
                try { shown = doc == null ? false : doc.GetIsShown(); } catch { }
                PluginLog.Write("schlib open=" + Client.IsDocumentOpen(libraryPath) + " shown=" + shown);
            }
            catch (Exception ex)
            {
                PluginLog.Write("open schlib error: " + ex.Message);
            }
        }

        // 直接发 SCH 自己的放置进程（IntegratedLibrary 包装层内部发的就是这条），
        // 不行再退回 IntegratedLibrary 命令。
        public static bool PlaceViaProcess(string libReference, string libraryPath)
        {
            try
            {
                var serverDoc = FindSchServerDocument();
                if (serverDoc == null) { ImportLog.Add("请先打开原理图"); return false; }

                EnsureLibraryOpen(libraryPath);

                var doc = ActivateSchDocument(serverDoc);
                if (doc == null || doc.GetState_ObjectId() == SCH.TObjectId.eSchLib)
                {
                    ImportLog.Add("当前是原理图库，不能放置");
                    return false;
                }

                var view = ResolveSchView(serverDoc);
                if (view == null) { ImportLog.Add("请先打开原理图"); return false; }
                FocusViewDocument(view);
                try { Client.SetCurrentView(view); } catch { }

                var launcher = ResolveProcessLauncher(view);
                PluginLog.Write("process launcher=" + (launcher == null ? "null" : launcher.GetType().FullName));
                if (launcher == null) return false;

                var parameters = "|PartId=|LibReference=" + libReference + "|Library=" + libraryPath +
                                 "|SourceLibraryName=" + Path.GetFileName(libraryPath) + "|";
                LogCommandState(launcher, view, "Sch:PlaceIntegratedComponent", parameters);

                var depthBefore = ProcessDepth();
                var started = DateTime.UtcNow;
                launcher.SendMessage("Sch:PlaceIntegratedComponent", ref parameters, view);
                var elapsed = (int)(DateTime.UtcNow - started).TotalMilliseconds;
                var depthAfter = ProcessDepth();
                PluginLog.Write("sch place elapsed=" + elapsed + "ms depth=" + depthBefore + "->" + depthAfter);
                if (elapsed >= 150 || depthAfter > depthBefore)
                {
                    ImportLog.Add("已进入放置状态：" + libReference);
                    return true;
                }

                PluginLog.Write("sch place returned immediately, trying integrated library");
                var fallback = "LibReference=" + libReference + "|Library=" + libraryPath;
                depthBefore = ProcessDepth();
                started = DateTime.UtcNow;
                launcher.SendMessage("IntegratedLibrary:PlaceLibraryComponent", ref fallback, view);
                elapsed = (int)(DateTime.UtcNow - started).TotalMilliseconds;
                depthAfter = ProcessDepth();
                PluginLog.Write("integrated place elapsed=" + elapsed + "ms depth=" + depthBefore + "->" + depthAfter);
                if (elapsed >= 150 || depthAfter > depthBefore)
                {
                    ImportLog.Add("已进入放置状态：" + libReference);
                    return true;
                }
                return false;
            }
            catch (Exception ex)
            {
                PluginLog.Write("process place exception: " + ex);
                return false;
            }
        }

        private static void LogCommandState(IProcessLauncher launcher, IServerDocumentView view,
                                            string command, string parameters)
        {
            try
            {
                bool first = false, second = false, third = false;
                string text1 = null, text2 = null;
                launcher.GetCommandState(command, ref parameters, view,
                    ref first, ref second, ref third, ref text1, ref text2);
                PluginLog.Write("state " + command + "=" + first + "," + second + "," + third +
                                " " + text1 + " " + text2);
            }
            catch (Exception ex)
            {
                PluginLog.Write("state " + command + " error: " + ex.Message);
            }
        }

        private static int ProcessDepth()
        {
            try
            {
                var control = Client as IProcessControl;
                if (control != null) return control.GetProcessDepth();
            }
            catch
            {
                // Process control is optional.
            }
            try
            {
                var control = Client.Internal_GetProcessControl() as IProcessControl;
                if (control != null) return control.GetProcessDepth();
            }
            catch
            {
                // Process control is optional.
            }
            return -1;
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
                var serverDoc = FindSchServerDocument();
                if (serverDoc == null) { ImportLog.Add("请先打开原理图"); return false; }
                var doc = ActivateSchDocument(serverDoc);
                if (doc == null || doc.GetState_ObjectId() == SCH.TObjectId.eSchLib)
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
                var serverDoc = FindSchServerDocument();
                if (serverDoc == null)
                {
                    ImportLog.Add("请先打开原理图");
                    return false;
                }
                var doc = ActivateSchDocument(serverDoc);
                if (doc == null || doc.GetState_ObjectId() == SCH.TObjectId.eSchLib)
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

                var view = ResolveSchView(serverDoc);
                PluginLog.Write("place interactive view=" + (view == null ? "null" : view.GetType().Name));
                if (view == null)
                {
                    ImportLog.Add("请先打开原理图");
                    return false;
                }
                FocusViewDocument(view);
                try { Client.SetCurrentView(view); } catch { }
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

        // 打开但不激活文档：导入库文件时避免界面在 PcbLib/SchLib 之间来回切换。
        // 若文档已经打开，则直接复用，避免把用户正在看的文档隐藏掉。
        public static IServerDocument EnsureDocumentHidden(string kind, string path)
        {
            try
            {
                if (Client.IsDocumentOpen(path))
                    return Client.Internal_GetDocumentByPath(path) as IServerDocument;
            }
            catch
            {
                // Fall through to opening it.
            }
            return OpenDocumentHidden(kind, path);
        }

        public static IServerDocument OpenDocumentHidden(string kind, string path)
        {
            try
            {
                var doc = Client.Internal_OpenDocumentShowOrHide(kind, path, false) as IServerDocument;
                if (doc != null)
                {
                    try { doc.SetIsShown(false); } catch { }
                }
                return doc;
            }
            catch
            {
                return OpenDocument(kind, path);
            }
        }
    }
}
