using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityGit.DiffView;

public class GitStatusPanel : GitPanel {
  // Messages...
  private static GUIContent UNSTAGED_CHANGES_LABEL = new GUIContent("Unstaged Changes:"),
                            STAGED_CHANGES_LABEL = new GUIContent("Staged Changes:"),
                            STAGE_CHANGES_BUTTON = new GUIContent("Stage Changed"),
                            SIGN_OFF_BUTTON = new GUIContent("Sign Off"),
                            COMMIT_BUTTON = new GUIContent("Commit"),
                            PUSH_BUTTON = new GUIContent("Push"),
                            AMEND_TOGGLE = new GUIContent("Amend");


  // Static helper data.
  private static Texture DEFAULT_FILE_ICON = EditorGUIUtility.ObjectContent(null, typeof(MonoScript)).image;
  private static GUIContent DUMMY_CONTENT = new GUIContent("X");
  private static GUILayoutOption ICON_WIDTH = GUILayout.Width(16),
                                 ITEM_HEIGHT = GUILayout.Height(16),
                                 MAX_ITEM_HEIGHT = GUILayout.MaxHeight(16);
  private static int ITEM_BASELINE = 2;


  // Overarching state for the panel.
  private GUIContent currentBranchLabel = new GUIContent("");
  private string currentBranch = null;
  private bool isDetachedHeadMode = false, isDirty = false;
  private GitWrapper.Change[] changes = null;


  // Event handlers.
  public override void OnEnable() {
    OnRefresh();
  }

  protected void Init() {
    // Set up data we can't set up until OnGUI.
    if(editorLineHeight <= 0) {
      editorLineHeight = GUI.skin.GetStyle("textarea").CalcHeight(DUMMY_CONTENT, 100);
    }
    if(isDirty) {
      OnRefresh();
      isDirty = false;
    }
  }

  public override void OnRefresh() {
    currentBranch = GitWrapper.CurrentBranch;
    // TODO: Refactor detection of detached-head state into GitWrapper.
    if(currentBranch == "") {
      currentBranchLabel.text = "Detached HEAD Mode";
      isDetachedHeadMode = true;
    } else {
      currentBranchLabel.text = currentBranch;
      isDetachedHeadMode = false;
    }

    changes = GitWrapper.Status;
  }


  // Operations.
  public void SignOff() {
    // TODO: Refactor signoff operation into GitWrapper.
    string signOffMessage = "Signed-off-by: " + GitWrapper.ConfigGet("user.name") + " <" + GitWrapper.ConfigGet("user.email") + ">";
    if(!commitMessage.EndsWith(signOffMessage))
      commitMessage += "\n" + signOffMessage;
  }

  public void StagePath(GitWrapper.ChangeType changeType, string path) {
    if(changeType == GitWrapper.ChangeType.Deleted) {
      GitWrapper.RemovePath(path);
    } else {
      GitWrapper.StagePath(path);
    }
    isDirty = true;
  }

  public void UnstagePath(GitWrapper.ChangeType changeType, string path) {
    GitWrapper.UnstagePath(path);
    isDirty = true;
  }


  // Helpers.
  protected delegate void WholeFileCommand(GitWrapper.ChangeType changeType, string path);
  protected delegate bool FilterDelegate(GitWrapper.Change change);
  protected delegate GitWrapper.ChangeType ChangeTypeDelegate(GitWrapper.Change change);

  public Color ColorForChangeType(GitWrapper.ChangeType status) {
    Color c = Color.red;
    switch(status) {
      case GitWrapper.ChangeType.Modified:  c = GitStyles.ModifiedColor; break;
      case GitWrapper.ChangeType.Added:     c = GitStyles.AddedColor; break;
      case GitWrapper.ChangeType.Deleted:   c = GitStyles.DeletedColor; break;
      case GitWrapper.ChangeType.Renamed:   c = GitStyles.RenamedColor; break;
      case GitWrapper.ChangeType.Copied:    c = GitStyles.CopiedColor; break;
      case GitWrapper.ChangeType.Untracked: c = GitStyles.UntrackedColor; break;
      default:
        Debug.Log("Should not have gotten this status: " + status);
        break;
    }
    return c;
  }

  public class ListSelectionState {
    private HashSet<string> selection = new HashSet<string>();
    private List<string> selectionWithOrder = new List<string>();

    public int Count {
      get {
        return selection.Count;
      }
    }

    public void Clear() {
      selection.Clear();
      selectionWithOrder.Clear();
    }

    public void Select(string path) {
      if(!selection.Contains(path)) {
        selection.Add(path);
        selectionWithOrder.Add(path);
      }
    }

    public void Unselect(string path) {
      if(selection.Contains(path)) {
        selection.Remove(path);
        selectionWithOrder.Remove(path);
      }
    }

    public bool IsSelected(string path) {
      return selection.Contains(path);
    }

    public void Set(string path, bool status) {
      if(status)
        Select(path);
      else
        Unselect(path);
    }

    public string Last {
      get {
        if(selectionWithOrder.Count > 0)
          return selectionWithOrder[selectionWithOrder.Count - 1];
        else
          return null;
      }
    }
  }

  public class ListView {
    private int _controlID;
    public int controlID {
      get { return _controlID; }
      set {
        if(_controlID != value) {
          Reset();
          _controlID = value;
        }
      }
    }
    public bool hasFocus = false;
    public ListSelectionState selection;

    public ListView() {
      Reset();
    }

    public void Reset() {
      hasFocus = false;
      selection = null;
    }

    //public void OnFocus() { Debug.Log("ListView.OnFocus"); }
    //public void OnLostFocus() { Debug.Log("ListView.OnLostFocus"); }

    public new string ToString() {
      return "id=" + controlID + ",hasFocus=" + hasFocus;
    }
  }

  private bool panelHasFocus = false;
  public override void OnFocus() { panelHasFocus = true; }
  public override void OnLostFocus() { panelHasFocus = false; }

  [System.NonSerialized]
  private Hashtable iconCache = new Hashtable();
  protected bool ShowFile(ListView state, string path, GitWrapper.ChangeType status, WholeFileCommand cmd) {
    Event current = Event.current;
    bool isChanged = false;
    bool isSelected = state.selection.IsSelected(path);
    GUIStyle style = isSelected ? (panelHasFocus && state.hasFocus ? GitStyles.FileLabelSelected : GitStyles.FileLabelSelectedUnfocused) : GitStyles.FileLabel;

    GUIContent tmp = null;
    if(!iconCache.ContainsKey(path)) {
      tmp = new GUIContent() {
        image = AssetDatabase.GetCachedIcon(path),
        text = null
      };
      if(tmp.image == null)
        tmp.image = DEFAULT_FILE_ICON;
      iconCache[path] = tmp;
    }
    tmp = (GUIContent)iconCache[path];

    GUILayout.BeginHorizontal();
      GUILayout.Label(tmp, style, ICON_WIDTH, ITEM_HEIGHT);
      Rect iconPosition = GUILayoutUtility.GetLastRect();

      Color c = GUI.contentColor;
      GUI.contentColor = ColorForChangeType(status);
      Rect labelPosition = EditorGUILayout.BeginVertical(style, MAX_ITEM_HEIGHT);
        GUILayout.FlexibleSpace();
        GUILayout.Label(path, style);
        GUILayout.Space(ITEM_BASELINE);
      EditorGUILayout.EndVertical();
      GUI.contentColor = c;
    GUILayout.EndHorizontal();

    if(current.type == EventType.MouseDown) {
      if(iconPosition.Contains(current.mousePosition)) {
        isChanged = true;
        cmd(status, path);
        state.selection.Unselect(path);
      } else if(labelPosition.Contains(current.mousePosition)) {
        isChanged = true;
        isSelected = !isSelected;
        bool addToSelection = false, rangeSelection = false;
        if(Event.current.command && Application.platform == RuntimePlatform.OSXEditor)
          addToSelection = true;
        else if(Event.current.control && Application.platform == RuntimePlatform.WindowsEditor)
          addToSelection = true;
        if(Event.current.shift)
          rangeSelection = true;

        if(!addToSelection && !rangeSelection)
          state.selection.Clear();
        state.selection.Set(path, isSelected);
        // TODO: For range selection we need the list of files, index of last selection, etc.
      }
    }

    return isChanged;
  }


  protected Vector2 FileListView(GUIContent label, Vector2 scrollPos, FilterDelegate filter, ChangeTypeDelegate changeTypeFetcher, WholeFileCommand cmd, ListSelectionState selectionCache) {
    int id = GUIUtility.GetControlID(FocusType.Passive);
    ListView state = (ListView)GUIUtility.GetStateObject(typeof(ListView), id);
    state.controlID = id;
    state.selection = selectionCache;

    Event current = Event.current;
    bool isChanged = false;

    GUILayout.Label(label, GitStyles.BoldLabel, GUIHelper.NoExpandWidth);
    Rect listPosition = EditorGUILayout.BeginVertical();
      scrollPos = EditorGUILayout.BeginScrollView(scrollPos, GitStyles.FileListBox);
        if(changes != null) {
          for(int i = 0; i < changes.Length; i++) {
            if(filter(changes[i])) {
              isChanged = isChanged || ShowFile(state, changes[i].path, changeTypeFetcher(changes[i]), cmd);
            }
          }
        }
      EditorGUILayout.EndScrollView();
    EditorGUILayout.EndVertical();

    switch(current.type) {
      case EventType.MouseDown:
        if(listPosition.Contains(current.mousePosition)) {
          GUIUtility.hotControl = id;
          GUIUtility.keyboardControl = id;
          current.Use();
        } else {
          if(GUIUtility.keyboardControl == id) {
            GUIUtility.keyboardControl = 0;
          }
        }
        break;
      case EventType.MouseUp:
        if(GUIUtility.hotControl == id) {
          // Done dragging...
          GUIUtility.hotControl = 0;
          current.Use();
        }
        break;
    }
    state.hasFocus = GUIUtility.keyboardControl == id;
    if(!state.hasFocus) {
      state.selection.Clear();
    }
    if(isChanged) {
      GUI.changed = true;
      current.Use();
    }

    return scrollPos;
  }

  protected bool WorkingSetFilter(GitWrapper.Change change) {
    return change.workingStatus != GitWrapper.ChangeType.Unmodified;
  }

  protected bool IndexSetFilter(GitWrapper.Change change) {
    return change.indexStatus != GitWrapper.ChangeType.Unmodified && change.indexStatus != GitWrapper.ChangeType.Untracked;
  }

  protected GitWrapper.ChangeType WorkingSetFetcher(GitWrapper.Change change) {
    return change.workingStatus;
  }

  protected GitWrapper.ChangeType IndexSetFetcher(GitWrapper.Change change) {
    return change.indexStatus;
  }


  // Sub-views.
  private ListSelectionState workingSetSelectionCache = new ListSelectionState();
  private Vector2 workingScrollPos;
  protected void ShowUnstagedChanges() {
    workingScrollPos = FileListView(UNSTAGED_CHANGES_LABEL, workingScrollPos, WorkingSetFilter, WorkingSetFetcher, StagePath, workingSetSelectionCache);
  }

  private ListSelectionState indexSetSelectionCache = new ListSelectionState();
  private Vector2 indexScrollPos;
  protected void ShowStagedChanges() {
    indexScrollPos = FileListView(STAGED_CHANGES_LABEL, indexScrollPos, IndexSetFilter, IndexSetFetcher, UnstagePath, indexSetSelectionCache);
  }

  private float editorLineHeight = -1;
  private string commitMessage = "";
  private bool amendCommit = false;
  protected void ShowCommitMessageEditor() {
    // TODO: Make this scrollable, and make it obey editor commands properly.
    // TODO: Clear selection cache as appropriate.
    commitMessage = GUILayout.TextArea(commitMessage, GUILayout.MinHeight(editorLineHeight * 9 + 2), GUIHelper.ExpandHeight);
    GUILayout.BeginHorizontal();
      GUILayout.Button(STAGE_CHANGES_BUTTON, GitStyles.CommandLeft);
      if(GUILayout.Button(SIGN_OFF_BUTTON, GitStyles.CommandMid))
        SignOff();
      GUILayout.Button(COMMIT_BUTTON, GitStyles.CommandMid);
      GUILayout.Button(PUSH_BUTTON, GitStyles.CommandRight);
      GUILayout.Space(10);
      amendCommit = GUILayout.Toggle(amendCommit, AMEND_TOGGLE);
      GUILayout.FlexibleSpace();
    GUILayout.EndHorizontal();
  }

  private Vector2 scrollPosition;
  private string lastSelectedPath = null;
  private string lastDiff = null;
  protected void ShowDiffView() {
    // TODO: Implement me!!!!
    if(workingSetSelectionCache.Count == 1) {
      if(workingSetSelectionCache.Last != lastSelectedPath) {
        lastSelectedPath = workingSetSelectionCache.Last;
        lastDiff = GitWrapper.GetDiff(lastSelectedPath, true);
      }
      GUILayout.BeginHorizontal(GitStyles.FileListBox);
        scrollPosition = DiffGUI.ScrollableWordDiff(scrollPosition, lastDiff, false);
      GUILayout.EndHorizontal();
    } else
      GUILayout.Box(GUIHelper.NoContent, GitStyles.FileListBox, GUIHelper.ExpandWidth, GUIHelper.ExpandHeight);
  }

  private VerticalPaneState changesConfiguration = new VerticalPaneState() {
    minPaneHeightTop = 100,
    minPaneHeightBottom = 100
  };
  private VerticalPaneState commitAndDiffConfiguration = new VerticalPaneState() {
    minPaneHeightTop = 75,
    minPaneHeightBottom = 180
  };
  private HorizontalPaneState overallConfiguration = new HorizontalPaneState() {
    minPaneWidthLeft = 150,
    minPaneWidthRight = 400,
    initialLeftPaneWidth = 220
  };
  public override void OnGUI() {
    Init();

    EditorGUILayoutHorizontalPanes.Begin(overallConfiguration);
      GUILayout.BeginVertical();
        EditorGUILayout.HelpBox(currentBranchLabel.text, isDetachedHeadMode ? MessageType.Warning : MessageType.Info, true);

        Space();

        EditorGUILayoutVerticalPanes.Begin(changesConfiguration);
          ShowUnstagedChanges();
        EditorGUILayoutVerticalPanes.Splitter();
          ShowStagedChanges();
        EditorGUILayoutVerticalPanes.End();
      GUILayout.EndVertical();
    EditorGUILayoutHorizontalPanes.Splitter();
      GUILayout.BeginVertical();
        EditorGUILayoutVerticalPanes.Begin(commitAndDiffConfiguration);
          ShowDiffView();
        EditorGUILayoutVerticalPanes.Splitter();
          ShowCommitMessageEditor();
        EditorGUILayoutVerticalPanes.End();
      GUILayout.EndVertical();
    EditorGUILayoutHorizontalPanes.End();
  }

  // Base constructor
  public GitStatusPanel(GitShell owner) : base(owner) {}
}
