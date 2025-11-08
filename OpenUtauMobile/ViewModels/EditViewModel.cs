using DynamicData;
using DynamicData.Binding;
using OpenUtau.Core;
using OpenUtau.Core.Ustx;
using OpenUtauMobile.Resources.Strings;
using OpenUtauMobile.Utils;
using OpenUtauMobile.Views.DrawableObjects;
using OpenUtauMobile.Views.Utils;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;
using Serilog;
using SkiaSharp;
using System.Diagnostics;
using System.Reactive.Linq;
using Preferences = OpenUtau.Core.Util.Preferences;

namespace OpenUtauMobile.ViewModels
{
    public partial class EditViewModel : ReactiveObject
    {
        /* UI 相关属性 */
        public double MainLayoutHeight { get; set; } = 1000d;
        public double MainEditHeight { get; set; } = 600d;
        public double PianoRollHeight { get; set; } = 800d;
        #region 走带 - 主编辑区 拖拽间隔
        [Reactive] public double DivPosY { get; set; } = 100d;
        [Reactive] public Rect BoundDiv { get; set; } = new Rect(0d, 100d, 1, 50d);
        [Reactive] public Rect BoundTrack { get; set; } = new Rect(0d, 0d, 1, 0d);
        [Reactive] public Rect BoundPianoRoll { get; set; } = new Rect(0d, 150d, 1, 0d);
        [Reactive] public Rect BoundDivControl { get; set; } = new Rect(0d, 100d, 60d, 50d);
        #endregion
        #region 钢琴卷帘 - 扩展区 拖拽间隔
        [Reactive] public double ExpHeight { get; set; } = 50d; // 不能直接用，因为是相对底部的高度
        [Reactive] public double DivExpPosY { get; set; } // 根据 ExpHeight 动态计算
        [Reactive] public Rect BoundExpDiv { get; set; } = new Rect(0d, 0d, 1, 50d); // 拖拽间隔
        [Reactive] public Rect BoundExp { get; set; } = new Rect(0d, 0d, 1, 0d); // 扩展区
        [Reactive] public Rect BoundExpDivControl { get; set; } = new Rect(0d, 0d, 60d, 50d); // 手柄
        #endregion
        #region Transformers
        public Transformer TrackTransformer { get; set; } = new();
        public Transformer PianoRollTransformer { get; set; } = new();
        #endregion
        public double Density => DeviceDisplay.MainDisplayInfo.Density;
        [Reactive] public double HeightPerTrack { get; set; } = 60d; // 每个轨道的高度
        [Reactive] public double HeightPerPianoKey { get; set; } = 40d; // 每个钢琴键的高度
        [Reactive] public bool IsOpenDetailedTrackHeader { get; set; } = false; // 是否打开详细轨道头部
        [Reactive] public double AvatarSize { get; set; } = 35d; // 头像大小
        [Reactive] public bool IsShowRemoveNoteButton { get; set; } = false; // 是否显示删除音符按钮
        [Reactive] public bool IsShowRenderPitchButton { get; set; } = false; // 是否显示渲染音高按钮
        public double OriginalVolume { get; set; } = 0d; // 保存原始音量
        public int[] SnapDivs = [4, 8, 16, 32, 64, 128, 3, 6, 12, 24, 48, 96, 192]; // 常用量化单位数组
        #region 编辑模式
        public enum TrackEditMode // 定义走带编辑模式（枚举类型）
        {
            // 只读模式
            Normal,
            // 编辑模式
            Edit,
        };
        public enum NoteEditMode // 定义音符编辑模式（枚举类型）
        {
            // 只读模式
            // Normal,
            // 音符编辑模式
            EditNote,
            // 音高曲线编辑模式
            EditPitchCurve,
            // 音高锚点编辑模式
            EditPitchAnchor,
            // 颤音编辑模式
            EditVibrato,
        };
        public enum ExpressionEditMode // 定义表达式编辑模式（枚举类型）
        {
            // 只读模式
            Hand,
            // 编辑模式
            Edit,
            // 橡皮擦模式
            Eraser,
        };
        /// <summary>
        /// 当前走带编辑模式
        /// </summary>
        [Reactive] public TrackEditMode CurrentTrackEditMode { get; set; } = TrackEditMode.Normal; // 默认为只读模式
        /// <summary>
        /// 当前钢琴卷帘音符编辑模式
        /// </summary>
        [Reactive] public NoteEditMode CurrentNoteEditMode { get; set; } = NoteEditMode.EditNote; // 默认为音符编辑模式
        /// <summary>
        /// 当前表情编辑模式
        /// </summary>
        [Reactive] public ExpressionEditMode CurrentExpressionEditMode { get; set; } = ExpressionEditMode.Hand; // 默认为手模式
        #endregion
        [Reactive] public ObservableCollectionExtended<UPart> PhonemizingParts { get; set; } = []; // 正在进行音素化的分片集合
        [Reactive] public string PhonemizingPartName { get; set; } = string.Empty; // 正在音素化的分片名称
        [Reactive] public bool IsPhonemizing { get; set; } = false; // 是否正在音素化
        [Reactive] public ObservableCollectionExtended<UPart> SelectedParts { get; set; } = []; // 选中的分片集合
        [Reactive] public string EditingPartName { get; set; } = string.Empty; // 正在编辑的分片名称
        [Reactive] public UVoicePart? EditingPart { get; set; } = null; // 正在编辑的分片
        [Reactive] public ObservableCollectionExtended<UNote> SelectedNotes { get; set; } = []; // 选中的音符集合
        /// <summary>
        /// 由SelectedNotes决定，请不要直接修改
        /// </summary>
        [Reactive] public UNote? EditingNote { get; set; } = null; // 正在编辑的音符，用于将来的属性面板
        #region 走带量化相关
        [Reactive] public bool IsTrackSnapToGrid { get; set; } = true; // 走带是否启用对齐网格
        [Reactive] public int TrackSnapDiv { get; set; } = 4; // 走带网格吸附密度 1/x 小节
        /// <summary>
        /// 当前走带量化状态下每个网格单位对应的tick长度
        /// </summary>
        public int TrackSnapUnitTick
        {
            get
            {
                if (TrackSnapDiv <= 0)
                {
                    return DocManager.Inst.Project.resolution * 4; // 默认返回一个小节的tick长度
                }
                return DocManager.Inst.Project.resolution * 4 / TrackSnapDiv;
            }
        } // 每个网格单位对应的tick长度
        #endregion
        #region 钢琴卷帘量化相关
        [Reactive] public bool IsPianoRollSnapToGrid { get; set; } = true; // 钢琴卷帘是否启用对齐网格
        [Reactive] public int PianoRollSnapDiv { get; set; } = 4; // 钢琴卷帘网格吸附密度 1/x 小节
        /// <summary>
        /// 当前钢琴卷帘量化状态下每个网格单位对应的tick长度
        /// </summary>
        public int PianoRollSnapUnitTick
        {
            get
            {
                if (PianoRollSnapDiv <= 0)
                {
                    return DocManager.Inst.Project.resolution * 4; // 默认返回一个小节的tick长度
                }
                return DocManager.Inst.Project.resolution * 4 / PianoRollSnapDiv;
            }
        } // 每个网格单位对应的tick长度
        #endregion
        #region 移动分片字段
        private SKPoint _startMovePartsPosition; // 用于记录开始拖动时的起始位置
        private int _startMovePartsTrackNo; // 用于记录开始拖动时的起始轨道号
        public bool IsMovingParts = false; // 是否正在移动分片
        private List<int> _oldMovedPartsPos = []; // 保存移动开始时的分片position列表
        private List<int> _oldMovedPartsTrackNo = []; // 保存移动开始时的分片trackNo列表
        #endregion
        #region 创建分片字段
        private SKPoint _startCreatePartPosition; // 用于记录开始创建分片时的起始位置
        public bool IsCreatingPart = false; // 是否正在创建分片
        #endregion
        #region 调整分片长度字段
        private SKPoint _startResizePartPosition; // 用于记录开始调整分片长度时的起始位置
        public bool IsResizingPart = false; // 是否正在调整分片长度
        private UPart? _resizingPart; // 正在调整长度的分片
        private int _resizingPartOriginalDuration; // 记录调整开始时的分片原始长度
        #endregion
        #region 调整音符长度字段
        private UNote? _resizingNote; // 用于记录正在调整长度的音符
        public bool IsResizingNote = false; // 是否正在调整音符长度
        private int _initialBound2TouchOffset = 0; // 记录开始调整音符长度时，音符右边界到触摸点（逻辑）的初始x偏移量
        #endregion
        #region 移动音符字段
        private SKPoint _startMoveNotesPosition; // 用于记录开始拖动音符时的起始位置
        public bool IsMovingNotes = false; // 是否正在移动音符
        private int _originalPosition; // 记录调整开始时的音符原始位置
        private int _startMoveNoteToneReversed; // 用于记录开始拖动音符时按住的那个音符的音高（不用减总琴键数）
        private int _offsetPosition; // 保存上一次移动后的总位置偏移量，用于计算相对移动
        private int _offsetTone; // 保存上一次移动后的总音高偏移量，用于计算相对移动
                                 //private List<int> _originalNoteTones; // 记录调整开始时的音符原始音高
        #endregion
        #region 音高曲线字段
        private int? _lastPitchTick; // 记录上一个音高点的tick位置
        private double? _lastPitchValue; // 记录上一个音高点的音高值
        #endregion
        #region 表情参数绘制相关状态字段
        private int _lastExpTick = 0;
        private int _lastExpValue = 0;
        private UExpressionDescriptor? _editingExpressionDescriptor;
        // 正在绘制的表情参数值
        public int currentExpressionValue = 0;
        #endregion
        /* 后端数据相关属性 */
        [Reactive] public string Path { get; set; } = string.Empty;
        [Reactive] public ObservableCollectionExtended<UTrack> Tracks { get; set; } = [];
        //[Reactive] public ObservableCollectionExtended<UPart> Parts { get; set; } = [];
        [Reactive] public int PlayPosTick { get; set; } = 0;
        [Reactive] public bool Playing { get; set; } = false;
        [Reactive] public byte[] CurrentPortrait { get; set; } = [];
        [Reactive] public double PortraitOpacity { get; set; } = 1d;
        public HashSet<DrawablePart> DrawableParts { get; set; } = [];
        /// <summary>
        /// 正在钢琴卷帘窗中编辑的可绘制音符组对象，null表示没有正在编辑的音符组
        /// </summary>
        public DrawableNotes? EditingNotes { get; set; }
        [Reactive] public bool PlayPosWaitingRendering { get; set; } = false; // 等待渲染
        public double OriginalPan { get; internal set; }
        [Reactive] public ObservableCollectionExtended<RunningWork> RunningWorks { get; set; } = []; // 正在运行的工作列表
        [Reactive] public UExpressionDescriptor PrimaryExpressionDescriptor { get; set; } = null!;
        [Reactive] public UExpressionDescriptor SecondaryExpressionDescriptor { get; set; } = null!;
        [Reactive] public string PrimaryExpressionAbbr { get; set; } = string.Empty;
        [Reactive] public string SecondaryExpressionAbbr { get; set; } = string.Empty;
        [Reactive] public Color EditingPartColor { get; set; } = Colors.Transparent; // 正在编辑的分片颜色
        public void InitExpressions()
        {
            PrimaryExpressionDescriptor = DocManager.Inst.Project.expressions.FirstOrDefault().Value;
            SecondaryExpressionDescriptor = DocManager.Inst.Project.expressions.Skip(1).FirstOrDefault().Value;
            UpdateExpressions();
        }
        public void UpdateExpressions()
        {
            PrimaryExpressionAbbr = PrimaryExpressionDescriptor.abbr;
            SecondaryExpressionAbbr = SecondaryExpressionDescriptor.abbr;
        }
        public void SetWork(WorkType type, string id, double progress = 0, string detail = "", CancellationTokenSource? cancellationTokenSource = null)
        {
            RunningWork? existingWork = RunningWorks.FirstOrDefault(w => w.Id == id);
            if (existingWork != null)
            {
                // 如果已经存在相同ID的工作，则更新其属性
                existingWork.Type = type;
                existingWork.Progress = progress;
                existingWork.Detail = detail;
                // 触发属性变化通知
                this.RaisePropertyChanged(nameof(RunningWorks));
                Debug.WriteLine($"更新工作 {id}：类型={type}, 进度={progress}, 详情={detail}");
            }
            else
            {
                // 否则添加新的工作
                RunningWorks.Add(new RunningWork
                {
                    Id = id,
                    Type = type,
                    Progress = progress,
                    Detail = detail,
                    CancellationTokenSource = cancellationTokenSource
                });
                Debug.WriteLine($"添加工作 {id}：类型={type}, 进度={progress}, 详情={detail}");
            }
        }

        public void RemoveWork(string id)
        {
            var workToRemove = RunningWorks.FirstOrDefault(w => w.Id == id);
            if (workToRemove != null)
            {
                RunningWorks.Remove(workToRemove);
                Debug.WriteLine($"移除工作 {id}");
            }
        }

        public void TryCancelWork(string id)
        {
            RunningWork? work = RunningWorks.FirstOrDefault(work => work.Id == id);
            if (work == null || work.CancellationTokenSource == null)
            {
                return;
            }
            work.CancellationTokenSource.Cancel();
            Debug.WriteLine($"取消工作 {id}");
        }



        public EditViewModel()
        {
            // 订阅 DivPosY 的变化
            this.WhenAnyValue(x => x.DivPosY)
                .Subscribe(_ => UpdateTrackMainEditBoundaries());
            // 订阅 ExpHeight 的变化
            this.WhenAnyValue(x => x.ExpHeight)
                .Subscribe(_ =>
                {
                    DivExpPosY = MainEditHeight - ExpHeight;
                    UpdatePianoRollExpBoundaries();
                });
            // 订阅正在音素化分片列表变化 - 监听集合变化
            PhonemizingParts.CollectionChanged += (sender, e) =>
            {
                IsPhonemizing = (PhonemizingParts.Count != 0);
                if (IsPhonemizing)
                {
                    PhonemizingPartName = AppResources.PhonemizingInProgress;
                    foreach (var part in PhonemizingParts)
                    {
                        if (part != null)
                        {
                            PhonemizingPartName += part.DisplayName + ' ';
                        }
                    }
                }
            };
            // 订阅选中分片列表变化
            SelectedParts.CollectionChanged += (sender, e) =>
            {
                IsShowRenderPitchButton = false; // 每次选中分片变化时重置渲染音高按钮显示状态
                if (SelectedParts.Count == 0)
                {
                    EditingPart = null; // 清空正在编辑的分片
                    EditingPartName = string.Empty; // 清空编辑分片名称
                    EditingNotes = null; // 清空正在编辑的音符组
                    CurrentPortrait = []; // 清空当前立绘
                    EditingPartColor = Colors.Transparent;
                }
                else
                {
                    // 设置正在编辑的分片为第一个选中的歌声分片
                    foreach (var part in SelectedParts)
                    {
                        if (part is UVoicePart voicePart)
                        {
                            EditingPart = voicePart;
                            UpdateIsShowRenderPitchButton();
                            LoadPortrait();
                            EditingPartColor = ViewConstants.TrackMauiColors[DocManager.Inst.Project.tracks[voicePart.trackNo].TrackColor];
                            break;
                        }
                    }
                    if (EditingPart == null)
                    {
                        EditingPartName = string.Empty; // 如果没有选中歌声分片，清空编辑分片名称
                        EditingNotes = null; // 清空正在编辑的音符组
                        return;
                    }
                    if (SelectedParts.Count == 1)
                    {
                        EditingPartName = EditingPart.DisplayName; // 更新编辑分片名称
                    }
                    else
                    {
                        EditingPartName = string.Format(AppResources.NPartsSelected, SelectedParts.Count); // 多个分片被选中时显示数量
                    }
                }
            };
            // 订阅选中音符列表变化 更新编辑音符
            SelectedNotes.CollectionChanged += (sender, e) => // 这个订阅有bug或者是我的问题，有时候失效，后面再看吧。先加个手动触发的函数
            {
                HandleSelectedNotesChanged();
            };
        }

        public void ValidateSelectedParts()
        {
            // 检查一遍选中的分片是否已经被删除
            SelectedParts.RemoveMany([.. SelectedParts
                    .Where(part =>
                    {
                        if (DocManager.Inst.Project.parts.Contains(part))
                        {
                            return false; // 如果分片还在项目中，则认为它是有效的
                        }
                        return true; // 如果分片不在项目中，则认为它是无效的
                    })]);
            if (SelectedParts.Count == 0)
            {
                EditingPart = null; // 清空正在编辑的分片
                EditingPartName = string.Empty; // 清空编辑分片名称
                EditingNotes = null; // 清空正在编辑的音符组
                CurrentPortrait = []; // 清空当前立绘
                EditingPartColor = Colors.Transparent;
            }
        }

        public void UpdateIsShowRenderPitchButton()
        {
            if (SelectedParts.Count == 0 || EditingPart == null)
            {
                IsShowRenderPitchButton = false; // 不显示渲染音高按钮
                return;
            }
            IsShowRenderPitchButton = DocManager.Inst.Project.tracks[EditingPart.trackNo].RendererSettings.Renderer?.SupportsRenderPitch ?? false; // 根据分片所属轨道的渲染器支持情况决定是否显示渲染音高按钮

        }

        public void UpdateTrackMainEditBoundaries()
        {
            BoundTrack = new Rect(BoundTrack.X, BoundTrack.Y, BoundTrack.Width, DivPosY); // 高度为 DivPosY
            BoundDiv = new Rect(BoundDiv.X, DivPosY, BoundDiv.Width, BoundDiv.Height); // 上边界为 DivPosY
            BoundPianoRoll = new Rect(BoundPianoRoll.X, DivPosY + 50d, BoundPianoRoll.Width, MainLayoutHeight - DivPosY - 50d); // 上边界为 DivPosY + 拖拽间隔高度（50），高度为 TotalHeight - DivPosY - 50d
            // todo : 限制expheight，防止超过MainEditHeight
        }
        public void UpdatePianoRollExpBoundaries()
        {
            BoundExpDiv = new Rect(BoundExpDiv.X, DivExpPosY, BoundExpDiv.Width, BoundExpDiv.Height); // 上边界为 DivExpPosY
            BoundExp = new Rect(BoundExp.X, DivExpPosY + 50d, BoundExp.Width, MainEditHeight - DivExpPosY - 50d); // 上边界为 DivExpPosY + 拖拽间隔高度（50），高度为 TotalHeight - DivExpPosY - 50d
        }

        public void HandleSelectedNotesChanged()
        {
            // 检查一遍选中的音符是否已经被删除
            SelectedNotes.RemoveMany([.. SelectedNotes
                    .Where(note =>
                    {
                        if (EditingPart == null)
                        {
                            return true; // 如果没有正在编辑的分片，则音符一定是无效的
                        }
                        return !EditingPart.notes.Contains(note); // 如果音符不在正在编辑的分片中，则认为它是无效的
                    })]);
            if (SelectedNotes.Count == 0) // 没有选中任何音符
            {
                EditingNote = null; // 清空正在编辑的音符
                IsShowRemoveNoteButton = false; // 不显示删除音符按钮
            }
            else
            {
                EditingNote = SelectedNotes[0]; // 设置正在编辑的音符为第一个选中的音符
                IsShowRemoveNoteButton = true; // 显示删除音符按钮
            }
        }

        public async Task Init()
        {
            await LoadProject(Path);
        }

        public static async Task LoadProject(string path)
        {
            await Task.Run(() =>
            {
                try
                {
                    // 新建
                    if (string.IsNullOrEmpty(path))
                    {
                        DocManager.Inst.ExecuteCmd(new LoadProjectNotification(OpenUtau.Core.Format.Ustx.Create()));
                    }
                    else
                    {
                        // 打开
                        string[] files = { path };
                        OpenUtau.Core.Format.Formats.LoadProject(files);
                    }
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Failed to load project.");
                    DocManager.Inst.ExecuteCmd(new ErrorMessageNotification(AppResources.ErrorFailLoadProject, ex));
                }
            });
        }


        public void SetBoundDivControl()
        {
            //BoundDivControl = new Rect(0d, DivPosY, 50d, 50d);
            BoundDivControl = new Rect(BoundDivControl.X, DivPosY, BoundDivControl.Width, BoundDivControl.Height);
        }

        public void SetBoundExpDivControl()
        {
            BoundExpDivControl = new Rect(BoundExpDivControl.X, DivExpPosY, BoundExpDivControl.Width, BoundExpDivControl.Height);
        }

        /// <summary>
        /// 开始移动分片
        /// </summary>
        /// <param name="parts"></param>
        /// <param name="startPosition">逻辑坐标</param>
        public void StartMoveParts(IEnumerable<UPart> parts, SKPoint startPosition)
        {
            if (parts == null || !parts.Any())
            {
                return;
            }
            _startMovePartsPosition = startPosition; // 记录开始拖动时的起始位置
            _startMovePartsTrackNo = (int)Math.Floor((startPosition.Y / Density) / HeightPerTrack);
            IsMovingParts = true; // 标记正在移动分片
            _oldMovedPartsPos.Clear(); // 清空之前的分片位置
            _oldMovedPartsTrackNo.Clear(); // 清空之前的分片轨道号
            foreach (var part in parts)
            {
                if (part == null)
                {
                    continue;
                }
                // 记录移动开始时的分片位置和轨道号
                _oldMovedPartsPos.Add(part.position);
                _oldMovedPartsTrackNo.Add(part.trackNo);
            }
            // 启动一个撤销组
            DocManager.Inst.StartUndoGroup();
        }

        /// <summary>
        /// 更新正在移动的分片位置
        /// </summary>
        /// <param name="currentPosition">逻辑坐标</param>
        public void UpdateMoveParts(SKPoint currentPosition)
        {
            if (!IsMovingParts)
            {
                return;
            }
            // 计算偏移量

            var offsetX = currentPosition.X - _startMovePartsPosition.X;
            var offsetY = currentPosition.Y - _startMovePartsPosition.Y;
            // 如果偏移量小于阈值，则不进行移动
            if (Math.Abs(offsetX) < 5)
            {
                return;
            }
            int deltaTrackNo = (int)Math.Floor((currentPosition.Y / Density) / HeightPerTrack) - _startMovePartsTrackNo;
            // 计算偏移量
            int[] newPositions = new int[SelectedParts.Count];
            int[] newTrackNos = new int[SelectedParts.Count];
            for (int i = 0; i < SelectedParts.Count; i++)
            {
                var part = SelectedParts[i];
                // 计算新的位置
                int newPosition = (int)(_oldMovedPartsPos[i] + offsetX);
                // 如果启用对齐网格，则将新的位置对齐到最近的网格线
                if (IsTrackSnapToGrid)
                {
                    newPosition = TrackTickToLinedTick(newPosition);
                    if (newPosition == part.position && deltaTrackNo == 0)
                    { return; }
                }
                int newTrackNo = _oldMovedPartsTrackNo[i] + deltaTrackNo;
                // 检查新的位置是否在有效范围内
                if (newPosition < 0 || newTrackNo < 0 || newTrackNo >= DocManager.Inst.Project.tracks.Count)
                {
                    Debug.WriteLine($"分片位置超出范围，无法移动。");
                    return; // 如果新的位置超出范围，则不进行移动
                }
                newPositions[i] = newPosition;
                newTrackNos[i] = newTrackNo;
            }
            // 更新选中分片的位置
            for (int i = 0; i < SelectedParts.Count; i++)
            {
                var part = SelectedParts[i];
                if (part == null)
                {
                    continue;
                }
                // 执行移动命令
                try
                {
                    DocManager.Inst.ExecuteCmd(new MovePartCommand(DocManager.Inst.Project, part, newPositions[i], newTrackNos[i]));
                }
                catch (Exception ex)
                {
                    Debug.WriteLine(ex);
                }
            }
        }

        /// <summary>
        /// 结束移动分片
        /// </summary>
        public void EndMoveParts()
        {
            IsMovingParts = false; // 重置移动状态
            // 结束撤销组
            DocManager.Inst.EndUndoGroup();
        }

        /// <summary>
        /// 开始创建分片
        /// </summary>
        /// <param name="currentPoint">逻辑坐标</param>
        public void StartCreatePart(SKPoint currentPoint)
        {
            _startCreatePartPosition = currentPoint; // 记录开始创建分片时的起始位置
            IsCreatingPart = true; // 标记正在创建分片
            int trackNo = (int)Math.Floor((currentPoint.Y / Density) / HeightPerTrack);
            // 确保轨道号在有效范围内
            if (trackNo < 0 || trackNo >= DocManager.Inst.Project.tracks.Count)
            {
                Debug.WriteLine($"轨道号 {trackNo} 超出范围，无法创建分片。");
                IsCreatingPart = false; // 创建失败，重置创建状态
                return;
            }
            int position = (int)currentPoint.X;
            if (IsTrackSnapToGrid)
            {
                position = TrackTickToFloorLinedTick(position);
            }
            // 获取一个初始分片
            UVoicePart part = new()
            {
                position = position,
                // 初始持续时间为一个网格对应的tick长度
                Duration = TrackSnapUnitTick,
                trackNo = trackNo,
                name = AppResources.NewPart
            };
            // 清除选中的分片
            SelectedParts.Clear();
            SelectedParts.Add(part);
            // 启动一个撤销组
            DocManager.Inst.StartUndoGroup();
            // 添加到项目中
            try
            {
                DocManager.Inst.ExecuteCmd(new AddPartCommand(DocManager.Inst.Project, part));
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex);
                IsCreatingPart = false; // 创建失败，重置创建状态
                DocManager.Inst.EndUndoGroup(); // 结束撤销组
                SelectedParts.Clear(); // 清空选中的分片
                // 清除out of range的分片
                foreach (var p in DocManager.Inst.Project.parts)
                {
                    if (p.trackNo >= DocManager.Inst.Project.tracks.Count || p.trackNo < 0)
                    {
                        DocManager.Inst.ExecuteCmd(new RemovePartCommand(DocManager.Inst.Project, p));
                    }
                }
            }
        }

        /// <summary>
        /// 更新正在创建的分片长度
        /// </summary>
        /// <param name="sKPoint"></param>
        public void UpdateCreatePart(SKPoint sKPoint)
        {
            if (!IsCreatingPart || SelectedParts == null || SelectedParts.Count == 0)
            {
                return;
            }
            int width = (int)(sKPoint.X - _startCreatePartPosition.X);
            if (IsTrackSnapToGrid)
            {
                width = TrackTickToLinedTick(SelectedParts[0].position + width) - SelectedParts[0].position;
                if (width == SelectedParts[0].Duration)
                { return; }
            }
            if (width <= 0)
            {
                return;
            }
            DocManager.Inst.ExecuteCmd(new ResizePartCommand(DocManager.Inst.Project, SelectedParts[0], width - SelectedParts[0].Duration, false));
        }

        /// <summary>
        /// 结束创建分片
        /// </summary>
        public void EndCreatePart()
        {
            IsCreatingPart = false; // 重置创建状态
            DocManager.Inst.EndUndoGroup(); // 结束撤销组
        }

        /// <summary>
        /// 删除选中的分片
        /// </summary>
        public void RemoveSelectedParts()
        {
            // 创建一个副本以避免在枚举期间修改集合
            List<UPart> partsToRemove = [.. SelectedParts];
            // 开启撤销组
            DocManager.Inst.StartUndoGroup();
            foreach (UPart part in partsToRemove)
            {
                if (part == null)
                {
                    continue;
                }
                try
                {
                    DocManager.Inst.ExecuteCmd(new RemovePartCommand(DocManager.Inst.Project, part));
                }
                catch (Exception ex)
                {
                    Debug.WriteLine(ex);
                    // 结束撤销组
                    DocManager.Inst.EndUndoGroup();
                    // 清空选中的分片
                    SelectedParts.Clear();
                }
            }
            // 结束撤销组
            DocManager.Inst.EndUndoGroup();
            // 清空选中的分片
            SelectedParts.Clear();
        }

        public static void AddTrack()
        {
            UTrack track = new UTrack(DocManager.Inst.Project)
            {
                TrackNo = DocManager.Inst.Project.tracks.Count,
                TrackName = AppResources.NewTrack,
                TrackColor = ViewConstants.TrackMauiColors.ElementAt(ObjectProvider.Random.Next(ViewConstants.TrackMauiColors.Count)).Key,
            };
            // 开启撤销组
            DocManager.Inst.StartUndoGroup();
            DocManager.Inst.ExecuteCmd(new AddTrackCommand(DocManager.Inst.Project, track));
            // 结束撤销组
            DocManager.Inst.EndUndoGroup();
        }

        internal void StartResizePart(UPart part, SKPoint sKPoint)
        {
            _startResizePartPosition = sKPoint; // 记录开始调整分片长度时的起始位置
            IsResizingPart = true; // 标记正在调整分片长度
            _resizingPart = part; // 记录正在调整长度的分片
            _resizingPartOriginalDuration = part.Duration; // 记录调整开始时的分片原始长度
            // 启动一个撤销组
            DocManager.Inst.StartUndoGroup();
        }

        internal void UpdateResizePart(SKPoint sKPoint)
        {
            if (!IsResizingPart || _resizingPart == null)
            {
                return;
            }
            int offsetX = (int)(sKPoint.X - _startResizePartPosition.X);
            int newWidth = Math.Max(0, _resizingPartOriginalDuration + offsetX);
            if (IsTrackSnapToGrid)
            {
                newWidth = TrackTickToLinedTick(_resizingPart.position + newWidth) - _resizingPart.position;
                if (newWidth == _resizingPart.Duration)
                { return; }
            }
            if (newWidth <= 0)
            {
                return;
            }
            DocManager.Inst.ExecuteCmd(new ResizePartCommand(DocManager.Inst.Project, _resizingPart, newWidth - _resizingPart.Duration, false));
        }

        internal void EndResizePart()
        {
            IsResizingPart = false; // 重置调整分片长度状态
            DocManager.Inst.EndUndoGroup(); // 结束撤销组
        }

        /// <summary>
        /// 走带获取最接近的对齐线tick位置
        /// </summary>
        /// <param name="tick"></param>
        /// <returns>已对齐的tick</returns>
        public int TrackTickToLinedTick(int tick)
        {
            if (tick < 0 || TrackSnapDiv <= 0)
            {
                return 0;
            }

            // 计算最接近的网格线位置
            int linedTick = (int)Math.Round((double)tick / TrackSnapUnitTick) * TrackSnapUnitTick;

            return linedTick;
        }

        /// <summary>
        /// 钢琴卷帘获取最接近的对齐线tick位置
        /// </summary>
        /// <param name="tick"></param>
        /// <returns>已对齐的tick</returns>
        public int PianoRollTickToLinedTick(int tick)
        {
            if (tick < 0 || PianoRollSnapDiv <= 0)
            {
                return 0;
            }

            // 计算最接近的网格线位置
            int linedTick = (int)Math.Round((double)tick / PianoRollSnapUnitTick) * PianoRollSnapUnitTick;

            return linedTick;
        }

        /// <summary>
        /// 钢琴卷帘获取前一个的对齐线tick位置
        /// </summary>
        /// <param name="tick"></param>
        /// <returns>已对齐的tick</returns>
        public int PianoRollTickToFloorLinedTick(int tick)
        {
            if (tick < 0 || PianoRollSnapDiv <= 0)
            {
                return 0;
            }

            // 计算最接近前一个的网格线位置
            int linedTick = ((int)Math.Floor((double)tick / PianoRollSnapUnitTick)) * PianoRollSnapUnitTick; // 这个括号好坑

            return linedTick;
        }

        /// <summary>
        /// 走带获取前一个的对齐线tick位置
        /// </summary>
        /// <param name="tick"></param>
        /// <returns>已对齐的tick</returns>
        public int TrackTickToFloorLinedTick(int tick)
        {
            if (tick < 0 || TrackSnapDiv <= 0)
            {
                return 0;
            }

            // 计算最接近前一个的网格线位置
            int linedTick = ((int)Math.Floor((double)tick / TrackSnapUnitTick)) * TrackSnapUnitTick;

            return linedTick;
        }

        /// <summary>
        /// 上移轨道
        /// </summary>
        /// <param name="track">要上移的轨道</param>
        /// <returns>是否移动成功</returns>
        public bool MoveTrackUp(UTrack track)
        {
            if (track == DocManager.Inst.Project.tracks.First())
            {
                return false;
            }
            DocManager.Inst.StartUndoGroup();
            DocManager.Inst.ExecuteCmd(new MoveTrackCommand(DocManager.Inst.Project, track, true));
            DocManager.Inst.EndUndoGroup();
            Tracks = [.. DocManager.Inst.Project.tracks];
            return true;
        }

        /// <summary>
        /// 下移轨道
        /// </summary>
        /// <param name="track">要下移的轨道</param>
        /// <returns>是否移动成功</returns>
        public bool MoveTrackDown(UTrack track)
        {
            if (track == DocManager.Inst.Project.tracks.Last())
            {
                return false;
            }
            DocManager.Inst.StartUndoGroup();
            DocManager.Inst.ExecuteCmd(new MoveTrackCommand(DocManager.Inst.Project, track, false));
            DocManager.Inst.EndUndoGroup();
            Tracks = [.. DocManager.Inst.Project.tracks];
            return true;
        }

        public void ToggleTrackMuted(UTrack track)
        {
            track.Muted = !track.Muted;
            Debug.WriteLine($"Track {track.TrackNo} Muted: {track.Muted}");
            RefreshTrack(track);
        }

        /// <summary>
        /// 创建一个默认音符
        /// </summary>
        /// <param name="currentPoint">逻辑坐标</param>
        public void CreateDefaultNote(SKPoint currentPoint)
        {
            int tone = (int)Math.Floor(ViewConstants.TotalPianoKeys - currentPoint.Y / Density / HeightPerPianoKey);
            // 确保音高在有效范围内
            if (tone < 0 || tone >= ViewConstants.TotalPianoKeys)
            {
                Debug.WriteLine($"音高 {tone} 超出范围，无法创建音符。");
                return;
            }
            try
            {
                if (EditingPart is UVoicePart voicePart)
                {
                    int position = PianoRollTickToFloorLinedTick((int)currentPoint.X);
                    // 确保位置在有效范围内
                    if (position < voicePart.position || position >= voicePart.End)
                    {
                        Debug.WriteLine($"位置 {position} 超出分片范围，无法创建音符。");
                        return;
                    }
                    // 获取一个初始音符
                    UNote note = DocManager.Inst.Project.CreateNote(
                        noteNum: tone,
                        posTick: position - voicePart.position,
                        durTick: PianoRollSnapUnitTick
                        );
                    note.lyric = "a";
                    // 清除选中的音符
                    SelectedNotes.Clear();
                    // 启动一个撤销组
                    DocManager.Inst.StartUndoGroup();
                    DocManager.Inst.ExecuteCmd(new AddNoteCommand(voicePart, note));
                    SelectedNotes.Add(note);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex);
                IsCreatingPart = false; // 创建失败，重置创建状态
                DocManager.Inst.EndUndoGroup(); // 结束撤销组
                SelectedNotes.Clear(); // 清空选中的音符
                // 清除out of range的音符
                //foreach (var p in DocManager.Inst.Project.tracks.)
                //{
                //    if (p.trackNo >= DocManager.Inst.Project.tracks.Count || p.trackNo < 0)
                //    {
                //        DocManager.Inst.ExecuteCmd(new RemoveNoteCommand(DocManager.Inst.Project, p));
                //    }
                //}
            }
            DocManager.Inst.EndUndoGroup(); // 结束撤销组
        }

        public void SetSinger(UTrack track, USinger newSinger)
        {
            if (track.Singer != newSinger)
            {
                DocManager.Inst.StartUndoGroup();
                DocManager.Inst.ExecuteCmd(new TrackChangeSingerCommand(DocManager.Inst.Project, track, newSinger));
                // 先尝试从偏好设置中设置用户常用的音素器
                if (!string.IsNullOrEmpty(newSinger?.Id) &&
                    OpenUtau.Core.Util.Preferences.Default.SingerPhonemizers.TryGetValue(newSinger.Id, out var phonemizerName) &&
                    TryChangePhonemizer(phonemizerName, track))
                {
                }
                else if (!string.IsNullOrEmpty(newSinger?.DefaultPhonemizer))
                { // 否则尝试设置音源默认的音素器
                    TryChangePhonemizer(newSinger.DefaultPhonemizer, track);
                }
                // 如果歌手切换失败
                if (newSinger == null || !newSinger.Found)
                {
                    // 重置为默认渲染器
                    var settings = new URenderSettings();
                    DocManager.Inst.ExecuteCmd(new TrackChangeRenderSettingCommand(DocManager.Inst.Project, track, settings));
                }
                else if (newSinger.SingerType != track.RendererSettings.Renderer?.SingerType)
                {
                    var settings = new URenderSettings
                    {
                        // 获取歌手类型对应的默认渲染器
                        renderer = OpenUtau.Core.Render.Renderers.GetDefaultRenderer(newSinger.SingerType),
                    };
                    DocManager.Inst.ExecuteCmd(new TrackChangeRenderSettingCommand(DocManager.Inst.Project, track, settings));
                }
                DocManager.Inst.ExecuteCmd(new VoiceColorRemappingNotification(track.TrackNo, true));
                DocManager.Inst.EndUndoGroup();
                // 更新最近使用的歌手列表
                if (!string.IsNullOrEmpty(newSinger?.Id) && newSinger.Found)
                {
                    OpenUtau.Core.Util.Preferences.Default.RecentSingers.Remove(newSinger.Id);
                    OpenUtau.Core.Util.Preferences.Default.RecentSingers.Insert(0, newSinger.Id);
                    if (OpenUtau.Core.Util.Preferences.Default.RecentSingers.Count > 16)
                    {
                        OpenUtau.Core.Util.Preferences.Default.RecentSingers.RemoveRange(
                            16, OpenUtau.Core.Util.Preferences.Default.RecentSingers.Count - 16);
                    }
                }
                OpenUtau.Core.Util.Preferences.Save();
                RefreshTrack(track);
                UpdateIsShowRenderPitchButton();
            }
        }

        private bool TryChangePhonemizer(string phonemizerName, UTrack track)
        {
            try
            {
                var factory = DocManager.Inst.PhonemizerFactories.FirstOrDefault(factory => factory.type.FullName == phonemizerName);
                var phonemizer = factory?.Create();
                if (phonemizer != null)
                {
                    DocManager.Inst.ExecuteCmd(new TrackChangePhonemizerCommand(DocManager.Inst.Project, track, phonemizer));
                    return true;
                }
            }
            catch (Exception e)
            {
                Serilog.Log.Error(e, $"未能加载音素器：{phonemizerName}");
            }
            return false;
        }

        public void ToggleDetailedTrackHeader()
        {
            IsOpenDetailedTrackHeader = !IsOpenDetailedTrackHeader;
            if (IsOpenDetailedTrackHeader)
            {
                HeightPerTrack = 100d;
                AvatarSize = 50d;
            }
            else
            {
                HeightPerTrack = 60d;
                AvatarSize = 35d;
            }
        }

        /// <summary>
        /// 刷新轨道（刷新全部轨道重载）
        /// </summary>
        public void RefreshTrack()
        {
            try
            {
                Tracks.Clear();
                Tracks = [.. DocManager.Inst.Project.tracks];
            }
            catch
            { }
        }

        /// <summary>
        /// 刷新轨道（刷新单个轨道重载）
        /// </summary>
        /// <param name="track">要更新的轨道</param>
        public void RefreshTrack(UTrack track)
        {
            try
            {
                int index = Tracks.IndexOf(track);
                if (index >= 0)
                {
                    Tracks.RemoveAt(index);
                    Tracks.Insert(index, track);
                }
            }
            catch
            { }
        }

        public void UpdateTrackVolume(UTrack track, double deltaVolume)
        {
            double newVolume = Math.Clamp(track.Volume + deltaVolume, -60.0, 12.0);
            if (newVolume != track.Volume)
            {
                track.Volume = newVolume;
                DocManager.Inst.ExecuteCmd(new VolumeChangeNotification(track.TrackNo, newVolume));
                Debug.WriteLine($"Track {track.TrackNo} Volume: {track.Volume}");
            }
        }

        public void RemoveNotes()
        {
            if (SelectedNotes.Count > 0 && EditingPart is UVoicePart part)
            {
                DocManager.Inst.StartUndoGroup();
                List<UNote> notesToRemove = SelectedNotes.ToList();
                DocManager.Inst.ExecuteCmd(new RemoveNoteCommand(part, notesToRemove));
                SelectedNotes.Clear();
                HandleSelectedNotesChanged();
                DocManager.Inst.EndUndoGroup();
            }
        }

        public void StartResizeNotes(SKPoint sKPoint, UNote resizingNote)
        {
            if (SelectedNotes.Count == 0 || SelectedNotes == null || EditingPart == null)
            {
                return;
            }
            _resizingNote = resizingNote; // 记录正在调整长度的音符
            IsResizingNote = true; // 标记正在调整音符长度
            _initialBound2TouchOffset = (int)sKPoint.X - (EditingPart.position + resizingNote.position + resizingNote.duration);
            // 启动一个撤销组
            DocManager.Inst.StartUndoGroup();
        }

        public void StartMoveNotes(SKPoint sKPoint)
        {
            if (EditingPart == null || SelectedNotes.Count == 0 || SelectedNotes == null)
            {
                return;
            }
            _startMoveNotesPosition = sKPoint; // 记录开始拖动音符时的起始位置
            IsMovingNotes = true; // 标记正在移动音符
            _originalPosition = SelectedNotes[0].position; // 记录调整开始时的第一个音符原始位置
            _startMoveNoteToneReversed = (int)Math.Floor(sKPoint.Y / Density / HeightPerPianoKey);
            _offsetPosition = 0; // 重置位置偏移量
            _offsetTone = 0; // 重置音高偏移量
            // 启动一个撤销组
            DocManager.Inst.StartUndoGroup();
        }

        internal void UpdateMoveNotes(SKPoint point)
        {
            if (!IsMovingNotes || SelectedNotes.Count == 0 || SelectedNotes == null || EditingPart == null)
            {
                return;
            }
            // 计算拖动距离
            float newOffsetPosition = point.X - _startMoveNotesPosition.X;
            int hoverToneReversed = (int)Math.Floor((float)(point.Y / Density / HeightPerPianoKey));
            if (IsPianoRollSnapToGrid) // 如果启用对齐网格，则将第一个音符的开头位置对齐到最近的网格线
            {
                int newPosition = PianoRollTickToLinedTick((int)(_originalPosition + newOffsetPosition));
                newOffsetPosition = newPosition - _originalPosition;
            }
            int deltaPosition = (int)newOffsetPosition - _offsetPosition;
            int newOffsetTone = _startMoveNoteToneReversed - hoverToneReversed;
            int deltaTone = newOffsetTone - _offsetTone;
            if (deltaPosition == 0 && deltaTone == 0)
            { return; } // 没有变化就不更新
            Debug.WriteLine($"deltaPosition: {newOffsetPosition}, deltaTone: {newOffsetTone}");
            // 更新音符位置
            DocManager.Inst.ExecuteCmd(new MoveNoteCommand(EditingPart, [.. SelectedNotes], deltaPosition, deltaTone));
            _offsetPosition = (int)newOffsetPosition;
            _offsetTone = newOffsetTone;
        }

        internal void UpdateResizeNotes(SKPoint point)
        {
            if (!IsResizingNote || SelectedNotes.Count == 0 || SelectedNotes == null || EditingPart == null || _resizingNote == null)
            {
                return;
            }
            int rightBound = EditingPart.position + _resizingNote.position + _resizingNote.duration;
            int deltaDuration = (int)(point.X - rightBound - _initialBound2TouchOffset);
            if (IsPianoRollSnapToGrid)
            {
                int snapedX = PianoRollTickToLinedTick((int)point.X - _initialBound2TouchOffset);
                deltaDuration = snapedX - rightBound;
            }
            if (deltaDuration == 0)
            { return; } // 没有变化就不更新
            Debug.WriteLine($"deltaDuration: {deltaDuration}");
            // 更新音符长度
            DocManager.Inst.ExecuteCmd(new ResizeNoteCommand(EditingPart, [.. SelectedNotes], deltaDuration));
        }

        public void EndMoveNotes()
        {
            IsMovingNotes = false; // 重置移动音符状态
            DocManager.Inst.EndUndoGroup(); // 结束撤销组
        }

        public void EndResizeNotes()
        {
            IsResizingNote = false; // 重置调整音符状态
            DocManager.Inst.EndUndoGroup(); // 结束撤销组
        }

        /// <summary>
        /// 尝试采样指定位置的已有音高（单位：cent）
        /// </summary>
        /// <param name="point">逻辑坐标</param>
        /// <returns>若音符存在则返回其音高，否则返回null</returns>
        public double? SamplePitch(SKPoint point)
        {
            if (EditingPart == null)
            {
                return null;
            }
            double tick = point.X;
            var note = EditingPart.notes.FirstOrDefault(n => n.End >= tick);
            if (note == null && EditingPart.notes.Count > 0)
            {
                note = EditingPart.notes.Last();
            }
            if (note == null)
            {
                return null;
            }
            double pitch = note.tone * 100;
            // 加上该音符内部的音高变化（从音符的pitch控制点采样）
            pitch += note.pitch.Sample(DocManager.Inst.Project, EditingPart, note, tick) ?? 0;
            // 特殊情况：如果下一个音符紧接着当前音符，且当前位置属于两个音符的过渡区域
            if (note.Next != null && note.Next.position == note.End)
            {
                double? delta = note.Next.pitch.Sample(DocManager.Inst.Project, EditingPart, note.Next, tick);
                if (delta != null)
                {
                    pitch += delta.Value + note.Next.tone * 100 - note.tone * 100;
                }
            }
            return pitch;
        }

        /// <summary>
        /// 将tick和double音高转化为视图逻辑坐标点
        /// </summary>
        /// <param name="tick">相对于整个项目的tick</param>
        /// <param name="pitch">音高</param>
        /// <returns>逻辑坐标</returns>
        public SKPoint PitchAndTickToPoint(int tick, double pitch)
        {
            return new SKPoint(
                x: tick,
                y: (float)((ViewConstants.TotalPianoKeys - pitch / 100 - 0.5f) * HeightPerPianoKey * Density)
            );
        }

        /// <summary>
        /// 将逻辑坐标点转化为tick和double音高
        /// </summary>
        /// <param name="point"></param>
        /// <param name="tick"></param>
        /// <param name="pitch"></param>
        public void PointToPitchAndTick(SKPoint point, out int tick, out double pitch)
        {
            tick = (int)point.X;
            pitch = 100 * (ViewConstants.TotalPianoKeys - 0.5d - point.Y / (HeightPerPianoKey * Density));
        }

        public void StartDrawPitch(SKPoint point)
        {
            if (EditingNotes == null)
            {
                return;
            }
            _lastPitchValue = null;
            _lastPitchTick = null;
            // 启动一个撤销组
            DocManager.Inst.StartUndoGroup();
        }

        /// <summary>
        /// 绘制音高曲线
        /// </summary>
        /// <param name="point">逻辑坐标</param>
        public void UpdateDrawPitch(SKPoint point)
        {
            if (EditingPart == null)
            {
                return;
            }

            // 获取当前点的tick位置和音高
            int tick = (int)point.X - EditingPart.position;
            PointToPitchAndTick(point, out _, out double expectedPitch);

            // 采样当前位置的基准音高
            SKPoint samplePoint = PitchAndTickToPoint(
                (int)Math.Round(tick / 5.0) * 5,
                expectedPitch);
            double? basePitch = SamplePitch(samplePoint);
            if (basePitch == null)
            {
                Debug.WriteLine($"无法采样基准音高，跳过绘制。");
                return;
            }
            //Debug.WriteLine($"采样基准音高：{basePitch} cent");

            // 计算当前音高与基准音高的差值(单位：cent)
            int pitchDelta = (int)Math.Round(expectedPitch - basePitch.Value);
            //Debug.WriteLine($"绘制音高点：tick={tick}, 期望音高={expectedPitch}, 差值={pitchDelta} cent");

            // 创建从上一个点到当前点的曲线
            DocManager.Inst.ExecuteCmd(new SetCurveCommand(
                DocManager.Inst.Project,
                EditingPart,
                OpenUtau.Core.Format.Ustx.PITD,
                tick,                 // 当前点位置
                pitchDelta,           // 当前点音高差值
                _lastPitchTick ?? tick,    // 上一个点位置（首次使用当前点）
                (int)(_lastPitchValue != null ? _lastPitchValue.Value : pitchDelta)  // 上一个点音高差值（首次使用当前差值）
            ));

            // 更新上一个点的信息，存储实际差值而非原始音高
            _lastPitchTick = tick;
            _lastPitchValue = pitchDelta;
        }

        /// <summary>
        /// 结束绘制音高曲线
        /// </summary>
        public void EndDrawPitch()
        {
            DocManager.Inst.EndUndoGroup(); // 结束撤销组
            _lastPitchValue = null;
            _lastPitchTick = null;
        }

        public void AddTempoSignature(int tick, double bpm)
        {
            DocManager.Inst.StartUndoGroup();
            DocManager.Inst.ExecuteCmd(new AddTempoChangeCommand(DocManager.Inst.Project, tick, bpm));
            DocManager.Inst.EndUndoGroup();
        }

        internal void AddTimeSignature(int bar, int barPerBeat, int barUnit)
        {
            DocManager.Inst.StartUndoGroup();
            DocManager.Inst.ExecuteCmd(new AddTimeSigCommand(DocManager.Inst.Project, bar, barPerBeat, barUnit));
            DocManager.Inst.EndUndoGroup();
        }

        public void ImportAudio(string file)
        {
            try
            {
                UProject project = DocManager.Inst.Project;
                UWavePart part = new()
                {
                    FilePath = file,
                };
                part.Load(project);
                if (part == null)
                {
                    return;
                }
                int trackNo = project.tracks.Count;
                part.trackNo = trackNo;
                DocManager.Inst.StartUndoGroup();
                DocManager.Inst.ExecuteCmd(new AddTrackCommand(project, new UTrack(project) { TrackNo = trackNo }));
                DocManager.Inst.ExecuteCmd(new AddPartCommand(project, part));
                DocManager.Inst.EndUndoGroup();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "导入音频失败");
            }
        }

        internal void RemoveTrack(UTrack track)
        {
            DocManager.Inst.StartUndoGroup();
            DocManager.Inst.ExecuteCmd(new RemoveTrackCommand(DocManager.Inst.Project, track));
            DocManager.Inst.EndUndoGroup();
            RefreshTrack();
        }

        internal void LoadPortrait()
        {
            //Debug.WriteLine("尝试加载歌手立绘");
            if (EditingPart == null)
            {
                CurrentPortrait = [];
                return;
            }
            if (OpenUtau.Core.Util.Preferences.Default.ShowPortrait)
            {
                //Debug.WriteLine($"尝试加载歌手立绘");
                if (DocManager.Inst.Project.tracks[EditingPart.trackNo].Singer is USinger singer && singer != null)
                {
                    CurrentPortrait = singer.LoadPortrait();
                    //Debug.WriteLine($"加载了歌手立绘 {singer.Name}, images size: {CurrentPortrait.Length}");
                    PortraitOpacity = Preferences.Default.CustomPortraitOptions ? Preferences.Default.PortraitOpacity : singer.PortraitOpacity;
                }
            }
        }

        public void SetTimeSignature(int beatPerBar, int beatUnit)
        {
            DocManager.Inst.StartUndoGroup();
            DocManager.Inst.ExecuteCmd(new TimeSignatureCommand(DocManager.Inst.Project, beatPerBar, beatUnit));
            DocManager.Inst.EndUndoGroup();
        }

        public void SetBpm(double bpm)
        {
            if (bpm == DocManager.Inst.Project.tempos[0].bpm)
            {
                return;
            }
            DocManager.Inst.StartUndoGroup();
            DocManager.Inst.ExecuteCmd(new BpmCommand(DocManager.Inst.Project, bpm));
            DocManager.Inst.EndUndoGroup();
        }

        public void SetKey(int key)
        {
            if (key == DocManager.Inst.Project.key)
            {
                return;
            }
            DocManager.Inst.StartUndoGroup();
            DocManager.Inst.ExecuteCmd(new KeyCommand(DocManager.Inst.Project, key));
            DocManager.Inst.EndUndoGroup();
        }

        public void RenderPitchAsync(
            UVoicePart part, List<UNote> selectedNotes,
            Action<string, int, int> setProgressCallback, CancellationToken cancellationToken, string workId)
        {
            Debug.WriteLine("开始渲染音高");
            UProject project = DocManager.Inst.Project;
            var renderer = project.tracks[part.trackNo].RendererSettings.Renderer; // 获取当前轨道的渲染器
            if (renderer == null || !renderer.SupportsRenderPitch)
            {
                DocManager.Inst.ExecuteCmd(new ErrorMessageNotification("Not supported")); // 弹出错误信息
                return;
            }
            var notes = selectedNotes.Count > 0 ? selectedNotes : part.notes.ToList(); // 获取选中的音符列表
            var positions = notes.Select(n => n.position + part.position).ToHashSet(); // 获取选中音符的绝对位置集合
            var phrases = part.renderPhrases.Where(phrase => phrase.notes.Any(n => positions.Contains(phrase.position + n.position))).ToArray(); // 获取包含选中音符的渲染phrase列表
            float minPitD = -1200;
            if (project.expressions.TryGetValue(OpenUtau.Core.Format.Ustx.PITD, out var descriptor))
            {
                minPitD = descriptor.min; // 获取PITD表情的最小值
            }

            int finished = 0;
            setProgressCallback(workId, 0, phrases.Length);
            List<SetCurveCommand> commands = new List<SetCurveCommand>();
            for (int ph_i = phrases.Count() - 1; ph_i >= 0; ph_i--)
            { // 遍历每个phrase
                Debug.WriteLine($"渲染音高 phrase {ph_i + 1}/{phrases.Length}");
                var phrase = phrases[ph_i];
                var result = renderer.LoadRenderedPitch(phrase);
                if (result == null)
                {
                    continue;
                }
                int? lastX = null;
                int? lastY = null;
                // TODO: Optimize interpolation and command. // todo: 优化插值和命令
                if (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
                // Take the first negative tick before start and the first tick after end for each segment; // 取每个片段开始前的第一个负刻度和结束后的第一个刻度
                // Reverse traversal, so that when the score slices are too close, priority is given to covering the consonant pitch of the next segment, reducing the impact on vowels.
                for (int i = 0; i < result.tones.Length; i++)
                {
                    if (result.tones[i] < 0)
                    {
                        continue;
                    }
                    int x = phrase.position - part.position + (int)result.ticks[i];
                    if (result.ticks[i] < 0)
                    {
                        if (i + 1 < result.ticks.Length && result.ticks[i + 1] > 0) { }
                        else
                            continue;
                    }
                    if (x >= phrase.position + phrase.duration)
                    {
                        i = result.tones.Length - 1;
                    }
                    int pitchIndex = Math.Clamp((x - (phrase.position - part.position - phrase.leading)) / 5, 0, phrase.pitches.Length - 1);
                    float basePitch = phrase.pitchesBeforeDeviation[pitchIndex];
                    int y = (int)(result.tones[i] * 100 - basePitch);
                    lastX ??= x;
                    lastY ??= y;
                    if (y > minPitD)
                    {
                        commands.Add(new SetCurveCommand(
                            project, part, OpenUtau.Core.Format.Ustx.PITD, x, y, lastX.Value, lastY.Value));
                    }
                    lastX = x;
                    lastY = y;
                }
                finished += 1;
                setProgressCallback(workId, finished, phrases.Length);
            }
            setProgressCallback(workId, phrases.Length, phrases.Length);

            DocManager.Inst.PostOnUIThread(() =>
            {
                DocManager.Inst.StartUndoGroup(true);
                commands.ForEach(DocManager.Inst.ExecuteCmd);
                DocManager.Inst.EndUndoGroup();
            });
        }

        public void ImportMidi(string file)
        {
            if (file == null)
            {
                return;
            }
            UProject project = DocManager.Inst.Project;
            List<UVoicePart> parts = OpenUtau.Core.Format.MidiWriter.Load(file, project);
            DocManager.Inst.StartUndoGroup();
            foreach (UVoicePart part in parts)
            {
                UTrack track = new(project)
                {
                    TrackNo = project.tracks.Count
                };
                part.trackNo = track.TrackNo;
                if (part.name != AppResources.NewPart)
                {
                    track.TrackName = part.name;
                }
                part.AfterLoad(project, track);
                DocManager.Inst.ExecuteCmd(new AddTrackCommand(project, track));
                DocManager.Inst.ExecuteCmd(new AddPartCommand(project, part));
            }
            DocManager.Inst.EndUndoGroup();
        }
        /// <summary>
        /// 开始绘制表情曲线
        /// </summary>
        /// <param name="point">实际坐标</param>
        /// <param name="canvasHeight">实际画布高度（未乘以Density）</param>
        public void StartDrawExpression(SKPoint point, float canvasHeight)
        {
            if (EditingPart == null)
            {
                return;
            }
            UProject project = DocManager.Inst.Project;
            UTrack track = DocManager.Inst.Project.tracks[EditingPart.trackNo];
            if (!track.TryGetExpDescriptor(project, PrimaryExpressionAbbr, out _editingExpressionDescriptor)) // 尝试从名称（如DYN）获取描述器
            {
                // 失败则清空描述器并返回
                _editingExpressionDescriptor = null;
                return;
            }
            if (_editingExpressionDescriptor.max <= _editingExpressionDescriptor.min)
            {
                // 无效的描述器
                return;
            }
            _lastExpTick = (int)PianoRollTransformer.ActualToLogicalX(point.X) - EditingPart.position;
            _lastExpValue = (int)(_editingExpressionDescriptor.max - point.Y * (_editingExpressionDescriptor.max - _editingExpressionDescriptor.min) / (float)canvasHeight / (float)Density);
            DocManager.Inst.StartUndoGroup();
        }
        /// <summary>
        /// 更新绘制表情曲线
        /// </summary>
        /// <param name="point">实际坐标</param>
        /// <param name="canvasHeight">实际画布高度（未乘以Density）</param>
        public void UpdateDrawExpression(SKPoint point, float canvasHeight)
        {
            if (EditingPart == null || _editingExpressionDescriptor == null)
            {
                return;
            }
            int currentTick = (int)PianoRollTransformer.ActualToLogicalX(point.X) - EditingPart.position;
            float currentValueExact = (_editingExpressionDescriptor.max - point.Y * (_editingExpressionDescriptor.max - _editingExpressionDescriptor.min) / (float)canvasHeight / (float)Density);
            int currentValue;
            if (_editingExpressionDescriptor.type == UExpressionType.Curve)
            {
                currentValue = (int)currentValueExact;
                currentExpressionValue = currentValue;
                UpdateDrawCurveExpression(currentTick, currentValue);
            }
            else
            {
                // 四舍五入取整
                currentValue = (int)Math.Round(currentValueExact);
                currentExpressionValue = currentValue;
                UpdateDrawPhonemeExp(currentTick, currentValue);
            }
            _lastExpTick = currentTick;
            _lastExpValue = currentValue;
        }
        /// <summary>
        /// 曲线型表情更新
        /// </summary>
        /// <param name="currentTick"></param>
        /// <param name="currentValue"></param>
        private void UpdateDrawCurveExpression(int currentTick, int currentValue)
        {
            if (EditingPart == null)
            {
                return;
            }
            DocManager.Inst.ExecuteCmd(new SetCurveCommand(
                DocManager.Inst.Project,
                EditingPart,
                PrimaryExpressionAbbr,
                currentTick,
                currentValue,
                _lastExpTick,
                _lastExpValue
            ));
        }
        /// <summary>
        /// 音素表情曲线
        /// </summary>
        /// <param name="currentTick"></param>
        /// <param name="currentValue"></param>
        private void UpdateDrawPhonemeExp(int currentTick, int currentValue)
        {
            if (EditingPart == null || _editingExpressionDescriptor == null)
            {
                return;
            }
            UProject project = DocManager.Inst.Project;
            UTrack track = DocManager.Inst.Project.tracks[EditingPart.trackNo];
            List<NoteHitInfo> hits = HitTestExpRange(_lastExpTick, currentTick);
            foreach (var hit in hits)
            {
                if (Preferences.Default.LockUnselectedNotesExpressions && SelectedNotes.Count > 0 && !SelectedNotes.Contains(hit.phoneme.Parent))
                {
                    continue;
                }
                float x = hit.note.position + hit.phoneme.position;
                // !!!只有在范围内的点才进行插值计算！！！
                int y = currentValue;
                if (x >= Math.Min(_lastExpTick, currentTick) && x <= Math.Max(_lastExpTick, currentTick))
                {
                    y = (int)Lerp(_lastExpTick, _lastExpValue, currentTick, currentValue, x);
                }
                y = Math.Clamp(y, (int)_editingExpressionDescriptor.min, (int)_editingExpressionDescriptor.max);

                float oldValue = hit.phoneme.GetExpression(DocManager.Inst.Project, track, PrimaryExpressionAbbr).Item1;
                if (y == (int)oldValue)
                {
                    continue;
                }
                DocManager.Inst.ExecuteCmd(new SetPhonemeExpressionCommand(
                        project,
                        track,
                        EditingPart, 
                        hit.phoneme, 
                        PrimaryExpressionAbbr,
                        y));
            }
        }
        /// <summary>
        /// 线性插值
        /// </summary>
        /// <param name="x1"></param>
        /// <param name="y1"></param>
        /// <param name="x2"></param>
        /// <param name="y2"></param>
        /// <param name="x"></param>
        /// <returns></returns>
        public static float Lerp(float x1, float y1, float x2, float y2, float x)
        {
            const float EPSILON = 1e-6f;
            if (Math.Abs(x2 - x1) < EPSILON)
            {
                return y1;
            }
            return y1 + (y2 - y1) * (x - x1) / (x2 - x1);
        }
        /// <summary>
        /// 结束绘制表情曲线
        /// </summary>
        public void EndDrawExpression()
        {
            DocManager.Inst.EndUndoGroup();
        }

        /// <summary>
        /// 根据指定范围命中音符
        /// </summary>
        /// <param name="tick1">起始</param>
        /// <param name="tick2">结束</param>
        /// <returns>命中信息列表</returns>
        public List<NoteHitInfo> HitTestExpRange(int tick1, int tick2)
        {
            if (tick1 > tick2)
            {
                (tick1, tick2) = (tick2, tick1);
            }
            var hits = new List<NoteHitInfo>();
            if (EditingPart == null)
            {
                return hits;
            }
            foreach (var phoneme in EditingPart.phonemes)
            {
                double leftBound = phoneme.position;
                double rightBound = phoneme.End;
                var note = phoneme.Parent;
                if (leftBound > tick2 || rightBound < tick1)
                {
                    continue;
                }
                int left = phoneme.position;
                int right = phoneme.End;
                if (left <= tick2 && tick1 <= right)
                {
                    hits.Add(new NoteHitInfo(note, phoneme)
                    {
                        hitX = true,
                    });
                }
            }
            return hits;
        }

        /// <summary>
        /// 命中音符信息
        /// </summary>
        public class NoteHitInfo
        {
            public UNote note;
            public UPhoneme phoneme;
            public bool hitBody;
            public bool hitResizeArea;
            public bool hitResizeAreaFromStart;
            public bool hitX;
            public NoteHitInfo(UNote note, UPhoneme phoneme)
            {
                this.note = note;
                this.phoneme = phoneme;
            }
            public NoteHitInfo(UNote note, UPhoneme phoneme, bool hitBody, bool hitResizeArea, bool hitResizeAreaFromStart, bool hitX)
            {
                this.note = note;
                this.phoneme = phoneme;
                this.hitBody = hitBody;
                this.hitResizeArea = hitResizeArea;
                this.hitResizeAreaFromStart = hitResizeAreaFromStart;
                this.hitX = hitX;
            }
        }
        /// <summary>
        /// 开始重置表情曲线
        /// </summary>
        /// <param name="point">实际坐标</param>
        public void StartResetExpression(SKPoint point)
        {
            if (EditingPart == null)
            {
                return;
            }
            UProject project = DocManager.Inst.Project;
            UTrack track = DocManager.Inst.Project.tracks[EditingPart.trackNo];
            if (!track.TryGetExpDescriptor(project, PrimaryExpressionAbbr, out _editingExpressionDescriptor)) // 尝试从名称（如DYN）获取描述器
            {
                // 失败则清空描述器并返回
                _editingExpressionDescriptor = null;
                return;
            }
            if (_editingExpressionDescriptor.max <= _editingExpressionDescriptor.min)
            {
                // 无效的描述器
                return;
            }
            _lastExpTick = (int)PianoRollTransformer.ActualToLogicalX(point.X) - EditingPart.position;
            //_lastExpValue = (int)(_editingExpressionDescriptor.max - point.Y * (_editingExpressionDescriptor.max - _editingExpressionDescriptor.min) / (float)canvasHeight / (float)Density);
            DocManager.Inst.StartUndoGroup();
        }
        /// <summary>
        /// 更新重置表情曲线
        /// </summary>
        /// <param name="point">实际坐标</param>
        public void UpdateResetExpression(SKPoint point)
        {
            if (EditingPart == null || _editingExpressionDescriptor == null)
            {
                return;
            }
            int currentTick = (int)PianoRollTransformer.ActualToLogicalX(point.X) - EditingPart.position;
            //float currentValueExact = (_editingExpressionDescriptor.max - point.Y * (_editingExpressionDescriptor.max - _editingExpressionDescriptor.min) / (float)canvasHeight / (float)Density);
            //int currentValue;
            if (_editingExpressionDescriptor.type == UExpressionType.Curve)
            {
                //currentValue = (int)currentValueExact;
                currentExpressionValue = (int)_editingExpressionDescriptor.defaultValue;
                UpdateResetCurveExpression(currentTick);
            }
            else
            {
                // 四舍五入取整
                //currentValue = (int)Math.Round(currentValueExact);
                currentExpressionValue = 0;
                UpdateResetPhonemeExp(currentTick);
            }
            _lastExpTick = currentTick;
            //_lastExpValue = currentValue;
        }
        /// <summary>
        /// 更新曲线型表情重置
        /// </summary>
        /// <param name="currentTick"></param>
        private void UpdateResetCurveExpression(int currentTick)
        {
            if (EditingPart == null || _editingExpressionDescriptor == null)
            {
                return;
            }
            DocManager.Inst.ExecuteCmd(new SetCurveCommand(
                DocManager.Inst.Project,
                EditingPart,
                PrimaryExpressionAbbr,
                currentTick,
                (int)_editingExpressionDescriptor.defaultValue,
                _lastExpTick,
                (int)_editingExpressionDescriptor.defaultValue
            ));
        }
        /// <summary>
        /// 更新音素表情曲线重置
        /// </summary>
        /// <param name="currentTick"></param>
        private void UpdateResetPhonemeExp(int currentTick)
        {
            if (EditingPart == null || _editingExpressionDescriptor == null)
            {
                return;
            }
            UProject project = DocManager.Inst.Project;
            UTrack track = DocManager.Inst.Project.tracks[EditingPart.trackNo];
            List<NoteHitInfo> hits = HitTestExpRange(_lastExpTick, currentTick);
            foreach (var hit in hits)
            {
                if (Preferences.Default.LockUnselectedNotesExpressions && SelectedNotes.Count > 0 && !SelectedNotes.Contains(hit.phoneme.Parent))
                {
                    continue;
                }
                //float x = hit.note.position + hit.phoneme.position;
                // !!!只有在范围内的点才进行插值计算！！！
                //int y = currentValue;
                //if (x > Math.Max(_lastExpTick, currentTick) && x < Math.Min(_lastExpTick, currentTick))
                //{
                //    y = (int)Lerp(_lastExpTick, _lastExpValue, currentTick, currentValue, x);
                //}
                //y = Math.Clamp(y, (int)_editingExpressionDescriptor.min, (int)_editingExpressionDescriptor.max);

                //float oldValue = hit.phoneme.GetExpression(DocManager.Inst.Project, track, PrimaryExpressionAbbr).Item1;
                //if (y == (int)oldValue)
                //{
                //    continue;
                //}
                DocManager.Inst.ExecuteCmd(new SetPhonemeExpressionCommand(
                        project,
                        track,
                        EditingPart,
                        hit.phoneme,
                        PrimaryExpressionAbbr,
                        null));
            }
        }
        /// <summary>
        /// 结束重置表情曲线
        /// </summary>
        public void EndResetExpression()
        {
            DocManager.Inst.EndUndoGroup();
        }
    }
}