using System;
using System.Collections.Generic;
using UnityEditor;

public static class GitWrapper {
  public static bool IsVersioningEnabled { get { return EditorSettings.externalVersionControl == ExternalVersionControl.Generic; } }
  public static bool IsVersioningIdeal { get { return EditorSettings.serializationMode == SerializationMode.ForceText; } }

  public static bool IsGitPresent { get { return GitBinary != null; } }

  private static bool _isWorking = true;
  public static bool IsWorking { get { return _isWorking; } }

  public static bool IsUsable { get { return IsGitPresent && IsVersioningEnabled && IsWorking; } }

  public static string CurrentBranch {
    get {
      try {
        string rawName = ShellHelpers.OutputFromCommand("git", "symbolic-ref --quiet HEAD");
        return ShellHelpers.OutputFromCommand("git", "for-each-ref --format=\"%(refname:short)\" --sort=refname:short -- " + QuoteRef(rawName));
      } catch {
        _isWorking = false;
        return null;
      }
    }
  }

  public static string GetDiff(string path, bool wordDiff) {
    try {
      string diffParams = wordDiff ? "--word-diff=porcelain " : "";
      return ShellHelpers.OutputFromCommand("git", "diff --submodule=log " + diffParams + "-- " + QuotePath(path));
    } catch {
      _isWorking = false;
      return null;
    }

  }

  public enum RefKind {
    Branch, TrackingBranch, Tag, Other
  }

  public struct Ref {
    // NOTE: shortName must be an UNAMBIGUOUS short name.
    public string fullName, shortName, id, upstream;
    private bool isInitialized;
    private RefKind _kind;
    public RefKind kind {
      get {
        if(!isInitialized) {
          isInitialized = true;
          if(fullName.StartsWith("refs/heads/"))
            _kind = RefKind.Branch;
          else if(fullName.StartsWith("refs/remotes/"))
            _kind = RefKind.TrackingBranch;
          else if(fullName.StartsWith("refs/tags/"))
            _kind = RefKind.Tag;
          else
            _kind = RefKind.Other;
        }
        return _kind;
      }
    }
  }

  public static Ref[] Refs {
    get {
      string tmp = ShellHelpers.OutputFromCommand("git", "for-each-ref --format=\"%(refname)%09%(refname:short)%09%(objectname)%09%(upstream)\"");
      string[] rawRefs = tmp.Split('\n');
      List<Ref> refs = new List<Ref>();
      foreach(string rawRef in rawRefs) {
        string[] fields = rawRef.Split('\t');
        Ref r = new Ref() {
          fullName = fields[0],
          shortName = fields[1],
          id = fields[2],
          upstream = String.IsNullOrEmpty(fields[3]) ? null : fields[3]
        };
        refs.Add(r);
      }
      return refs.ToArray();
    }
  }

  public static string Version {
    get {
      try {
        return ShellHelpers.OutputFromCommand("git", "--version").Replace("git version ", "");
      } catch {
        _isWorking = false;
        return null;
      }
    }
  }

  public enum ChangeType {
    Unmodified,
    Modified,
    Added,
    Deleted,
    Renamed,
    Copied,
    UpdatedButUnmerged,
    Untracked
  }

  public struct Change {
    // TODO: Make this identify merge conflicts.
    public ChangeType indexStatus, workingStatus;
    public string path;
  }

  /*
         The output from this command is designed to be used as a commit template comment, and all the output lines are prefixed with #. The default, long
         format, is designed to be human readable, verbose and descriptive. They are subject to change in any time.

         The paths mentioned in the output, unlike many other git commands, are made relative to the current directory if you are working in a subdirectory (this
         is on purpose, to help cutting and pasting). See the status.relativePaths config option below.

         In short-format, the status of each path is shown as

             XY PATH1 -> PATH2

         where PATH1 is the path in the HEAD, and -> PATH2 part is shown only when PATH1 corresponds to a different path in the index/worktree (i.e. the file is
         renamed). The XY is a two-letter status code.

         The fields (including the ->) are separated from each other by a single space. If a filename contains whitespace or other nonprintable characters, that
         field will be quoted in the manner of a C string literal: surrounded by ASCII double quote (34) characters, and with interior special characters
         backslash-escaped.

         For paths with merge conflicts, X and Y show the modification states of each side of the merge. For paths that do not have merge conflicts, X shows the
         status of the index, and Y shows the status of the work tree. For untracked paths, XY are ??. Other status codes can be interpreted as follows:

         o      = unmodified
         o    M = modified
         o    A = added
         o    D = deleted
         o    R = renamed
         o    C = copied
         o    U = updated but unmerged

         Ignored files are not listed.

             X          Y     Meaning
             -------------------------------------------------
                       [MD]   not updated
             M        [ MD]   updated in index
             A        [ MD]   added to index
             D         [ M]   deleted from index
             R        [ MD]   renamed in index
             C        [ MD]   copied in index
             [MARC]           index and work tree matches
             [ MARC]     M    work tree changed since index
             [ MARC]     D    deleted in work tree
             -------------------------------------------------
             D           D    unmerged, both deleted
             A           U    unmerged, added by us
             U           D    unmerged, deleted by them
             U           A    unmerged, added by them
             D           U    unmerged, deleted by us
             A           A    unmerged, both added
             U           U    unmerged, both modified
             -------------------------------------------------
             ?           ?    untracked
             -------------------------------------------------

         If -b is used the short-format status is preceded by a line

         ## branchname tracking info

         There is an alternate -z format recommended for machine parsing. In that format, the status field is the same, but some other things change. First, the
         -> is omitted from rename entries and the field order is reversed (e.g from -> to becomes to from). Second, a NUL (ASCII 0) follows each filename,
         replacing space as a field separator and the terminating newline (but a space still separates the status field from the first filename). Third,
         filenames containing special characters are not specially formatted; no quoting or backslash-escaping is performed. Fourth, there is no branch line.
  */
  private static ChangeType ChangeTypeFromChar(char c) {
    switch(c) {
      case ' ': return ChangeType.Unmodified;
      case 'M': return ChangeType.Modified;
      case 'A': return ChangeType.Added;
      case 'D': return ChangeType.Deleted;
      case 'R': return ChangeType.Renamed;
      case 'C': return ChangeType.Copied;
      case 'U': return ChangeType.UpdatedButUnmerged;
      case '?': return ChangeType.Untracked;
      default: throw new ArgumentException("Character '" + c + "' is not a valid change type.");
    }
  }

  public static Change[] Status {
    get {
      List<Change> changes = new List<Change>();
      try {
        string tmp = ShellHelpers.OutputFromCommand("git", "status --porcelain --untracked-files=all -z");
        string[] records = tmp.Split('\0');
        for(int i = 0; i < records.Length; i++) {
          ChangeType iStatus = ChangeTypeFromChar(records[i][0]);
          ChangeType wStatus = ChangeTypeFromChar(records[i][1]);

          if(iStatus == ChangeType.Renamed) {
            // Need to consume a couple entries here...
            changes.Add(new Change() {
              indexStatus = ChangeTypeFromChar('A'),
              workingStatus = wStatus,
              path = records[i].Substring(3)
            });
            changes.Add(new Change() {
              indexStatus = ChangeTypeFromChar('D'),
              workingStatus = wStatus,
              path = records[i + 1]
            });
            i++;
          } else {
            changes.Add(new Change() {
              indexStatus = iStatus,
              workingStatus = wStatus,
              path = records[i].Substring(3)
            });
          }
        }
      } catch {
        // TODO: Hrm....
      }
      return changes.ToArray();
    }
  }

  private static string QuoteRef(string refName) {
    if(refName.IndexOf("'") != -1)
      throw new ArgumentException("We don't take kindly to refs with single-quotes in them, such as: \"" + refName + "\".  TODO: Fix this.");
    return "'" + refName + "'";
  }

  private static string QuotePath(string path) {
    if(path.IndexOf("'") != -1)
      throw new ArgumentException("We don't take kindly to paths with single-quotes in them, such as: \"" + path + "\".  TODO: Fix this.");
    return "'" + path + "'";
  }

  public static void StagePath(string path) {
    ShellHelpers.OutputFromCommand("git", "add --ignore-errors -- " + QuotePath(path));
  }

  public static void RemovePath(string path) {
    ShellHelpers.OutputFromCommand("git", "rm -- " + QuotePath(path));
  }

  public static void UnstagePath(string path) {
    ShellHelpers.OutputFromCommand("git", "reset HEAD -- " + QuotePath(path));
  }

  public static string ConfigGet(string key) {
    try {
      return ShellHelpers.OutputFromCommand("git", "config --get " + key);
    } catch {
      _isWorking = false;
      return null;
    }
  }

  private static string _gitBinary = null;
  public static string GitBinary {
    get {
      // TODO: This is OSX-specific.  We should fix that.
      try {
        if(_gitBinary == null)
          _gitBinary =  ShellHelpers.OutputFromCommand("which", "git");
        return _gitBinary;
      } catch {
        return null;
      }
    }
  }
}
