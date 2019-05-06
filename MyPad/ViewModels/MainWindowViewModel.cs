﻿using Dragablz;
using ICSharpCode.AvalonEdit.Highlighting.Xshd;
using MyLib.Wpf.Interactions;
using MyPad.Models;
using MyPad.Properties;
using Prism.Commands;
using Prism.Interactivity.InteractionRequest;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;

namespace MyPad.ViewModels
{
    public class MainWindowViewModel : ViewModelBase
    {
        #region スタティック

        private static int SEQUENSE = 0;

        #endregion

        #region リクエスト

        public InteractionRequest<MessageNotification> MessageRequest { get; } = new InteractionRequest<MessageNotification>();
        public InteractionRequest<OpenFileNotificationEx> OpenFileRequest { get; } = new InteractionRequest<OpenFileNotificationEx>();
        public InteractionRequest<SaveFileNotificationEx> SaveFileRequest { get; } = new InteractionRequest<SaveFileNotificationEx>();
        public InteractionRequest<PrintDocumentNotification> PrintRequest { get; } = new InteractionRequest<PrintDocumentNotification>();
        public InteractionRequest<TransitionNotification> TransitionRequest { get; } = new InteractionRequest<TransitionNotification>();

        #endregion

        #region プロパティ

        public int Sequense { get; } = ++SEQUENSE;

        public bool IsModified => this.Editors.Any(c => c.IsModified);

        private bool _isWorking;
        public bool IsWorking
        {
            get => this._isWorking;
            set => this.SetProperty(ref this._isWorking, value);
        }

        private FlowDocument _flowDocument;
        public FlowDocument FlowDocument
        {
            get => this._flowDocument;
            set => this.SetProperty(ref this._flowDocument, value);
        }

        private TextEditorViewModel _activeEditor;
        public TextEditorViewModel ActiveEditor
        {
            get => this._activeEditor;
            set => this.SetProperty(ref this._activeEditor, value);
        }

        private TerminalViewModel _activeTerminal;
        public TerminalViewModel ActiveTerminal
        {
            get => this._activeTerminal;
            set => this.SetProperty(ref this._activeTerminal, value);
        }

        public ObservableCollection<TextEditorViewModel> Editors { get; } = new ObservableCollection<TextEditorViewModel>();

        public ObservableCollection<TerminalViewModel> Terminals { get; } = new ObservableCollection<TerminalViewModel>();

        public Func<TextEditorViewModel> EditorFactory { get; } = () => new TextEditorViewModel();

        public Func<TerminalViewModel> TerminalFactory =>
            () =>
            {
                var terminal = new TerminalViewModel();
                terminal.Disposed += this.Terminal_Disposed;
                return terminal;
            };

        #endregion

        #region コマンド

        public ICommand AddCommand
            => new DelegateCommand(() => this.AddEditor());

        public ICommand OpenCommand
            => new DelegateCommand(async () => await this.LoadEditor());

        public ICommand SaveCommand
            => new DelegateCommand(async () => await this.SaveEditor(this.ActiveEditor));

        public ICommand SaveAsCommand
            => new DelegateCommand(async () => await this.SaveAsEditor(this.ActiveEditor));

        public ICommand SaveAllCommand
            => new DelegateCommand(async () =>
            {
                for (var i = 0; i < this.Editors.Count; i++)
                {
                    if (this.Editors[i].IsReadOnly)
                        continue;
                    if (await this.SaveEditor(this.Editors[i]) == false)
                        return;
                }
            });

        public ICommand PrintPreviewCommand
            => new DelegateCommand(async () => this.FlowDocument = await this.ActiveEditor.CreateFlowDocument());

        public ICommand PrintCommand
            => new DelegateCommand(() => this.PrintRequest.Raise(new PrintDocumentNotification(this.FlowDocument)));

        public ICommand InitializeXshdCommand
            => new DelegateCommand(() =>
            {
                this.MessageRequest.Raise(
                    new MessageNotification(Resources.Message_ConfirmInitializeXshd, MessageKind.CancelableWarning),
                    n =>
                    {
                        if (n.Result == true && ResourceService.CleanUpXshd())
                            ResourceService.InitializeXshd(true);
                    });
            });

        public ICommand CloseCommand
            => new DelegateCommand(async () =>
            {
                if (await this.SaveChangesIfAndRemove(this.ActiveEditor) == false)
                    return;
                if (this.Editors.Any() == false)
                    this.AddEditor();
            });

        public ICommand CloseAllCommand
            => new DelegateCommand(async () =>
            {
                if (await this.SaveChangesIfAndRemove() == false)
                    return;
                if (this.Editors.Any() == false)
                    this.AddEditor();
            });

        public ICommand CloseOtherCommand
            => new DelegateCommand(async () =>
            {
                var currentEditor = this.ActiveEditor;
                for (var i = this.Editors.Count - 1; 0 <= i; i--)
                {
                    if (this.Editors[i].Equals(currentEditor))
                        continue;
                    if (await this.SaveChangesIfAndRemove(this.Editors[i]) == false)
                        return;
                }
            });

        public ICommand ActivateCommand
            => new DelegateCommand<TextEditorViewModel>(editor =>
            {
                if (editor != null && this.Editors.Contains(editor))
                    this.ActiveEditor = editor;
                else
                    this.TransitionRequest.Raise(new TransitionNotification(TransitionKind.Activate));
            });

        public ICommand ReloadCommand
            => new DelegateCommand<Tuple<Encoding, string>>(async tuple =>
                await this.ReloadEditor(this.ActiveEditor, tuple.Item1, tuple.Item2));

        public ICommand AddTerminalCommand
            => new DelegateCommand(() => this.AddTerminal());

        public ICommand CloseTerminalCommand
            => new DelegateCommand(() =>
            {
                if (this.ActiveTerminal != null)
                    this.RemoveTerminal(this.ActiveTerminal);
            });

        public ICommand ActivatedHandler
            => new DelegateCommand<EventArgs>(e =>
            {
                if (WorkspaceViewModel.Instance != null)
                    WorkspaceViewModel.Instance.ActiveWindow = this;
            });

        public ICommand DropHandler
            => new DelegateCommand<DragEventArgs>(async e =>
            {
                if (e.Data.GetData(DataFormats.FileDrop) is IEnumerable<string> paths && paths.Any())
                {
                    await this.LoadEditor(paths);
                    e.Handled = true;
                }
            });

        public ICommand ClosingHandler
            => new DelegateCommand<CancelEventArgs>(async e =>
            {
                if (e.Cancel || this.IsModified == false)
                    return;

                e.Cancel = true;
                if (await this.SaveChangesIfAndRemove())
                    this.Dispose();
            });

        public Delegate ClosingContentHandler
            => new ItemActionCallback(async e =>
            {
                if (e.IsCancelled || !(e.DragablzItem?.DataContext is TextEditorViewModel content))
                    return;
                if (await this.SaveChangesIfAndRemove(content) == false)
                    e.Cancel();
                if (this.Editors.Any() == false)
                    this.AddEditor();
            });

        #endregion

        #region メソッド

        public MainWindowViewModel()
        {
            BindingOperations.EnableCollectionSynchronization(this.Editors, new object());
            BindingOperations.EnableCollectionSynchronization(this.Terminals, new object());
        }

        protected override void Dispose(bool disposing)
        {
            for (var i = this.Editors.Count - 1; 0 <= i; i--)
                this.RemoveEditor(this.Editors[i]);
            for (var i = this.Terminals.Count - 1; 0 <= i; i--)
                this.RemoveTerminal(this.Terminals[i]);
            base.Dispose(disposing);
        }

        public TextEditorViewModel AddEditor()
        {
            var editor = this.EditorFactory.Invoke();
            this.AddEditor(editor);
            return editor;
        }

        public void AddEditor(TextEditorViewModel editor)
        {
            this.Editors.Add(editor);
            this.ActiveEditor = editor;
        }

        public bool RemoveEditor(TextEditorViewModel editor)
        {
            if (this.Editors.Contains(editor) == false)
                return false;

            this.Editors.Remove(editor);
            editor.Dispose();
            return true;
        }

        public async Task LoadEditor(IEnumerable<string> paths = null)
        {
            bool decideConditions(string root, out IEnumerable<string> fileNames, out string filter, out Encoding encoding)
            {
                // 起点位置を設定する
                // - ディレクトリが指定されている場合は、それを起点とする
                // - ファイルが指定されている場合は、それを含む階層を起点とする
                // - いずれでも無い場合は、既定値とする
                if (Directory.Exists(root) == false)
                {
                    if (File.Exists(root))
                        root = Path.GetDirectoryName(root);
                    else
                        root = string.Empty;
                }

                // ダイアログを表示し、ファイルのパスと読み込み条件を選択させる
                var ready = false;
                IEnumerable<string> fn = null;
                string f = null;
                Encoding e = null;
                this.OpenFileRequest.Raise(
                    new OpenFileNotificationEx()
                    {
                        DefaultDirectory = root,
                        Encoding = SettingsService.Instance.System.AutoDetectEncoding ? null : SettingsService.Instance.System.Encoding,
                    },
                    n =>
                    {
                        if (n.Result == true)
                        {
                            ready = true;
                            fn = n.FileNames;
                            f = n.FilterName;
                            e = n.Encoding;
                        }
                    });

                // 戻り値を設定する
                fileNames = fn;
                filter = f;
                encoding = e;
                return ready;
            }

            if (paths == null)
            {
                // パスが指定されていない場合
                // - ファイルパスを選択させて読み込む

                if (decideConditions(null, out var fileNames, out var filter, out var encoding) == false)
                    return;

                foreach (var path in fileNames)
                {
                    var definition = Consts.SYNTAX_DEFINITIONS.ContainsKey(filter) ?
                        Consts.SYNTAX_DEFINITIONS[filter] :
                        Consts.SYNTAX_DEFINITIONS.Values.FirstOrDefault(d => d.Extensions.Contains(Path.GetExtension(path)));
                    var editor = await this.ReadFile(path, encoding, definition);
                    if (editor != null)
                        this.ActiveEditor = editor;
                }
            }
            else
            {
                // パスが指定されている場合
                // - ファイルパスの場合は、そのまま読み込む
                // - ディレクトリパスの場合は、ファイルパスを選択させて読み込む

                foreach (var path in paths.Where(path => File.Exists(path)))
                {
                    var encoding = SettingsService.Instance.System.AutoDetectEncoding ? null : SettingsService.Instance.System.Encoding;
                    var definition = Consts.SYNTAX_DEFINITIONS.Values.FirstOrDefault(d => d.Extensions.Contains(Path.GetExtension(path)));
                    var editor = await this.ReadFile(path, encoding, definition);
                    if (editor != null)
                        this.ActiveEditor = editor;
                }

                foreach (var path in paths.Where(path => Directory.Exists(path)))
                {
                    if (decideConditions(path, out var fileNames, out var filter, out var encoding) == false)
                        continue;

                    foreach (var fileName in fileNames)
                    {
                        var definition = Consts.SYNTAX_DEFINITIONS.ContainsKey(filter) ?
                            Consts.SYNTAX_DEFINITIONS[filter] :
                            Consts.SYNTAX_DEFINITIONS.Values.FirstOrDefault(d => d.Extensions.Contains(Path.GetExtension(fileName)));
                        var editor = await this.ReadFile(fileName, encoding, definition);
                        if (editor != null)
                            this.ActiveEditor = editor;
                    }
                }
            }
        }

        public async Task<bool> ReloadEditor(TextEditorViewModel editor, Encoding encoding, string lanugage = "")
        {
            var definition =
                string.IsNullOrEmpty(lanugage) ? null :
                Consts.SYNTAX_DEFINITIONS.ContainsKey(lanugage) ? Consts.SYNTAX_DEFINITIONS[lanugage] : null;
            if (editor.IsNewFile)
            {
                editor.Encoding = encoding;
                editor.SyntaxDefinition = definition;
                return true;
            }
            else
            {
                return await this.ReadFile(editor.FileName, encoding, definition) != null;
            }
        }

        private async Task<TextEditorViewModel> ReadFile(string path, Encoding encoding, XshdSyntaxDefinition definition)
        {
            if (string.IsNullOrEmpty(path))
                throw new ArgumentException("空のパスが指定されています。", nameof(path));

            var sameEditor = this.Editors.FirstOrDefault(m => m.FileName.Equals(path));
            if (sameEditor != null)
            {
                // 同名ファイルを占有しているコンテンツをアクティブにする
                this.ActiveEditor = sameEditor;

                // 文字コードが異なる場合はリロードする
                if (sameEditor.Encoding?.Equals(encoding) != true)
                {
                    // 変更がある場合は確認する
                    if (sameEditor.IsModified)
                    {
                        var r = false;
                        this.MessageRequest.Raise(
                            new MessageNotification(Resources.Message_ConfirmDiscardChanges, MessageKind.CancelableWarning),
                            n => r = n.Result == true);
                        if (r == false)
                            return null;
                    }

                    // 指定された文字コードでリロードする
                    try
                    {
                        this.IsWorking = true;
                        await sameEditor.Reload(encoding);
                    }
                    catch (Exception e)
                    {
                        this.MessageRequest.Raise(new MessageNotification(e.Message, MessageKind.Error));
                        return null;
                    }
                    finally
                    {
                        this.IsWorking = false;
                    }
                }

                // シンタックス定義を設定する
                sameEditor.SyntaxDefinition = definition;
                return sameEditor;
            }
            else
            {
                // 他のウィンドウが同名ファイルを占有している場合は処理を委譲する
                if (WorkspaceViewModel.Instance.DelegateReloadEditor(this, path, encoding))
                    return null;

                // ファイルサイズを確認する
                var info = new FileInfo(path);
                if (AppConfig.LargeFileSize <= info.Length)
                {
                    var ready = false;
                    this.MessageRequest.Raise(
                        new MessageNotification(Resources.Message_ConfirmOpenLargeFile, MessageKind.CancelableWarning),
                        n => ready = n.Result == true);
                    if (ready == false)
                        return null;
                }

                // ストリームを取得する
                FileStream stream = null;
                try
                {
                    // 可能であれば読み取りと書き込みの権限を取得する
                    stream = info.Open(FileMode.Open, FileAccess.ReadWrite, FileShare.Read);
                }
                catch
                {
                    // 失敗した場合は読み取り権限のみを取得する
                    try
                    {
                        stream = info.Open(FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                        this.MessageRequest.Raise(new MessageNotification(Resources.Message_NotifyFileLocked, path, MessageKind.Warning));
                    }
                    catch (Exception e)
                    {
                        this.MessageRequest.Raise(new MessageNotification(e.Message, MessageKind.Error));
                        return null;
                    }
                }

                // ファイルを読み込む
                TextEditorViewModel editor;
                try
                {
                    this.IsWorking = true;
                    editor = this.AddEditor();
                    await editor.Load(stream, encoding);
                }
                catch (Exception e)
                {
                    this.MessageRequest.Raise(new MessageNotification(e.Message, MessageKind.Error));
                    return null;
                }
                finally
                {
                    this.IsWorking = false;
                }

                // シンタックス定義を設定する
                editor.SyntaxDefinition = definition;
                return editor;
            }
        }

        private async Task<bool> SaveEditor(TextEditorViewModel editor)
        {
            if (editor.IsNewFile || editor.IsReadOnly)
                return await this.SaveAsEditor(editor);
            else
                return await this.WriteFile(editor, editor.FileName, editor.Encoding, editor.SyntaxDefinition);
        }

        private async Task<bool> SaveAsEditor(TextEditorViewModel editor)
        {
            this.ActiveEditor = editor;

            var ready = false;
            var path = editor.FileName;
            var filter = editor.SyntaxDefinition?.Name;
            var encoding = editor.Encoding;
            this.SaveFileRequest.Raise(
                new SaveFileNotificationEx()
                {
                    DefaultDirectory = editor.IsNewFile ? string.Empty : Path.GetDirectoryName(path),
                    FileName = Path.GetFileName(path),
                    FilterName = filter,
                    Encoding = encoding,
                },
                n =>
                {
                    if (n.Result == true)
                    {
                        ready = true;
                        path = n.FileName;
                        filter = n.FilterName;
                        encoding = n.Encoding;
                    }
                });
            if (ready == false)
                return false;

            var definition = Consts.SYNTAX_DEFINITIONS.Values.FirstOrDefault(d => d.Extensions.Contains(Path.GetExtension(path)));
            return await this.WriteFile(editor, path, encoding, definition);
        }

        public async Task<bool> SaveChangesIfAndRemove(TextEditorViewModel editor)
        {
            if (editor.IsModified)
            {
                this.ActiveEditor = editor;

                bool? result = null;
                this.MessageRequest.Raise(
                    new MessageNotification(Resources.Message_ConfirmSaveChanges, editor.FileName, MessageKind.CancelableConfirm),
                    n => result = n.Result);
                switch (result)
                {
                    case true:
                        if (await this.SaveEditor(editor) == false)
                            return false;
                        break;
                    case false:
                        break;
                    default:
                        return false;
                }
            }

            this.RemoveEditor(editor);
            return true;
        }

        public async Task<bool> SaveChangesIfAndRemove()
        {
            for (var i = this.Editors.Count - 1; 0 <= i; i--)
            {
                if (await this.SaveChangesIfAndRemove(this.Editors[i]) == false)
                    return false;
            }
            return true;
        }

        private async Task<bool> WriteFile(TextEditorViewModel editor, string path, Encoding encoding, XshdSyntaxDefinition definition)
        {
            if (string.IsNullOrEmpty(path))
                throw new ArgumentException("空のパスが渡されました。", nameof(path));

            var sameEditor = this.Editors.FirstOrDefault(m => m.FileName.Equals(path));
            if (sameEditor != null)
            {
                // 他のコンテンツが同名ファイルを占有している場合は、保存せずに終了する
                if (sameEditor.Equals(editor) == false || sameEditor.IsReadOnly)
                {
                    this.ActiveEditor = sameEditor;
                    this.MessageRequest.Raise(new MessageNotification(Resources.Message_NotifyFileLocked, sameEditor.FileName, MessageKind.Warning));
                    return false;
                }

                // ファイルに保存する
                try
                {
                    this.IsWorking = true;
                    await sameEditor.Save(encoding);
                }
                catch (Exception e)
                {
                    this.MessageRequest.Raise(new MessageNotification(e.Message, MessageKind.Error));
                    return false;
                }
                finally
                {
                    this.IsWorking = false;
                }

                // シンタックス定義を設定する
                sameEditor.SyntaxDefinition = definition;
                return true;
            }
            else
            {
                // 他のウィンドウが同名ファイルを占有している場合は、保存せずに終了する
                var otherSameEditor = WorkspaceViewModel.Instance.DelegateActivateEditor(this, path);
                if (otherSameEditor != null)
                {
                    this.MessageRequest.Raise(new MessageNotification(Resources.Message_NotifyFileLocked, otherSameEditor.FileName, MessageKind.Warning));
                    return false;
                }

                // ストリームを取得し、ファイルに保存する
                FileStream stream = null;
                try
                {
                    this.IsWorking = true;
                    Directory.CreateDirectory(Path.GetDirectoryName(path));
                    stream = new FileStream(path, FileMode.Create, FileAccess.ReadWrite, FileShare.Read);
                    await editor.SaveAs(stream, encoding);
                }
                catch (Exception e)
                {
                    this.MessageRequest.Raise(new MessageNotification(e.Message, MessageKind.Error));
                    return false;
                }
                finally
                {
                    this.IsWorking = false;
                }

                // シンタックス定義を設定する
                editor.SyntaxDefinition = definition;
                return true;
            }
        }

        private TerminalViewModel AddTerminal()
        {
            var terminal = this.TerminalFactory.Invoke();
            terminal.Start();
            this.Terminals.Add(terminal);
            this.ActiveTerminal = terminal;
            return terminal;
        }

        private bool RemoveTerminal(TerminalViewModel terminal)
        {
            var i = this.Terminals.IndexOf(terminal);
            if (i < 0)
                return false;

            this.Terminals.RemoveAt(i);
            terminal.Disposed -= this.Terminal_Disposed;
            terminal.Dispose();
            if (this.Terminals.Any())
                this.ActiveTerminal = this.Terminals[Math.Max(i - 1, 0)];
            return true;
        }

        private void Terminal_Disposed(object sender, EventArgs e)
        {
            this.RemoveTerminal((TerminalViewModel)sender);
        }

        #endregion
    }
}
