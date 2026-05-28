namespace Meziantou.Framework.SyntaxHighlighting.Tests;

public class GitConfigHighlighterTests
{

    [Fact]
    public void Section_User()
    {
        AssertHighlighter("gitconfig",
"""
[user]
""",
"""
<span class="hljs-section">[user]</span>
""");
    }

    [Fact]
    public void Section_Core()
    {
        AssertHighlighter("gitconfig",
"""
[core]
""",
"""
<span class="hljs-section">[core]</span>
""");
    }

    [Fact]
    public void Section_Alias()
    {
        AssertHighlighter("gitconfig",
"""
[alias]
""",
"""
<span class="hljs-section">[alias]</span>
""");
    }

    [Fact]
    public void Section_Color()
    {
        AssertHighlighter("gitconfig",
"""
[color]
""",
"""
<span class="hljs-section">[color]</span>
""");
    }

    [Fact]
    public void Section_Push()
    {
        AssertHighlighter("gitconfig",
"""
[push]
""",
"""
<span class="hljs-section">[push]</span>
""");
    }

    [Fact]
    public void Section_Pull()
    {
        AssertHighlighter("gitconfig",
"""
[pull]
""",
"""
<span class="hljs-section">[pull]</span>
""");
    }

    [Fact]
    public void Section_Merge()
    {
        AssertHighlighter("gitconfig",
"""
[merge]
""",
"""
<span class="hljs-section">[merge]</span>
""");
    }

    [Fact]
    public void Section_Diff()
    {
        AssertHighlighter("gitconfig",
"""
[diff]
""",
"""
<span class="hljs-section">[diff]</span>
""");
    }

    [Fact]
    public void Section_Branch()
    {
        AssertHighlighter("gitconfig",
"""
[branch]
""",
"""
<span class="hljs-section">[branch]</span>
""");
    }

    [Fact]
    public void Section_BranchNamed()
    {
        AssertHighlighter("gitconfig",
"""
[branch "main"]
""",
"""
<span class="hljs-section">[branch &quot;main&quot;]</span>
""");
    }

    [Fact]
    public void Section_RemoteNamed()
    {
        AssertHighlighter("gitconfig",
"""
[remote "origin"]
""",
"""
<span class="hljs-section">[remote &quot;origin&quot;]</span>
""");
    }

    [Fact]
    public void Section_IncludeIf()
    {
        AssertHighlighter("gitconfig",
"""
[includeIf "gitdir:~/work/"]
""",
"""
<span class="hljs-section">[includeIf &quot;gitdir:~/work/&quot;]</span>
""");
    }

    [Fact]
    public void Section_Submodule()
    {
        AssertHighlighter("gitconfig",
"""
[submodule "vendor/lib"]
""",
"""
<span class="hljs-section">[submodule &quot;vendor/lib&quot;]</span>
""");
    }

    [Fact]
    public void Section_CredentialNamed()
    {
        AssertHighlighter("gitconfig",
"""
[credential "https://github.com"]
""",
"""
<span class="hljs-section">[credential &quot;https://github.com&quot;]</span>
""");
    }

    [Fact]
    public void CoreSettings_AutoCrlf()
    {
        AssertHighlighter("gitconfig",
"""
[core]
  autocrlf = input
""",
"""
<span class="hljs-section">[core]</span>
  <span class="hljs-attr">autocrlf</span> = input
""");
    }

    [Fact]
    public void CoreSettings_Editor()
    {
        AssertHighlighter("gitconfig",
"""
[core]
  editor = vim
""",
"""
<span class="hljs-section">[core]</span>
  <span class="hljs-attr">editor</span> = vim
""");
    }

    [Fact]
    public void CoreSettings_Pager()
    {
        AssertHighlighter("gitconfig",
"""
[core]
  pager = less -FRX
""",
"""
<span class="hljs-section">[core]</span>
  <span class="hljs-attr">pager</span> = less -FRX
""");
    }

    [Fact]
    public void CoreSettings_ExcludesFile()
    {
        AssertHighlighter("gitconfig",
"""
[core]
  excludesFile = ~/.gitignore_global
""",
"""
<span class="hljs-section">[core]</span>
  <span class="hljs-attr">excludesFile</span> = ~/.gitignore_global
""");
    }

    [Fact]
    public void CoreSettings_FileMode()
    {
        AssertHighlighter("gitconfig",
"""
[core]
  fileMode = false
""",
"""
<span class="hljs-section">[core]</span>
  <span class="hljs-attr">fileMode</span> = false
""");
    }

    [Fact]
    public void CoreSettings_IgnoreCase()
    {
        AssertHighlighter("gitconfig",
"""
[core]
  ignoreCase = false
""",
"""
<span class="hljs-section">[core]</span>
  <span class="hljs-attr">ignoreCase</span> = false
""");
    }

    [Fact]
    public void CoreSettings_CommitGraph()
    {
        AssertHighlighter("gitconfig",
"""
[core]
  commitGraph = true
""",
"""
<span class="hljs-section">[core]</span>
  <span class="hljs-attr">commitGraph</span> = true
""");
    }

    [Fact]
    public void UserSettings_Identity()
    {
        AssertHighlighter("gitconfig",
"""
[user]
  name = Alice Example
  email = alice@example.com
""",
"""
<span class="hljs-section">[user]</span>
  <span class="hljs-attr">name</span> = Alice Example
  <span class="hljs-attr">email</span> = alice@example.com
""");
    }

    [Fact]
    public void UserSettings_SigningKey()
    {
        AssertHighlighter("gitconfig",
"""
[user]
  signingKey = ABC123DEF456
""",
"""
<span class="hljs-section">[user]</span>
  <span class="hljs-attr">signingKey</span> = ABC123DEF456
""");
    }

    [Fact]
    public void Alias_StatusShortcut()
    {
        AssertHighlighter("gitconfig",
"""
[alias]
  st = status
""",
"""
<span class="hljs-section">[alias]</span>
  <span class="hljs-attr">st</span> = status
""");
    }

    [Fact]
    public void Alias_Multiple()
    {
        AssertHighlighter("gitconfig",
"""
[alias]
  st = status
  co = checkout
  br = branch
  ci = commit
""",
"""
<span class="hljs-section">[alias]</span>
  <span class="hljs-attr">st</span> = status
  <span class="hljs-attr">co</span> = checkout
  <span class="hljs-attr">br</span> = branch
  <span class="hljs-attr">ci</span> = commit
""");
    }

    [Fact]
    public void Alias_WithFlags()
    {
        AssertHighlighter("gitconfig",
"""
[alias]
  unstage = reset HEAD --
""",
"""
<span class="hljs-section">[alias]</span>
  <span class="hljs-attr">unstage</span> = reset HEAD --
""");
    }

    [Fact]
    public void Alias_PrettyLog()
    {
        AssertHighlighter("gitconfig",
"""
[alias]
  lg = log --graph --pretty=format:'%C(yellow)%h%Creset %s%Creset %C(cyan)(%an)%Creset' --abbrev-commit
""",
"""
<span class="hljs-section">[alias]</span>
  <span class="hljs-attr">lg</span> = log --graph --pretty=format:&#x27;%C(yellow)%h%Creset %s%Creset %C(cyan)(%an)%Creset&#x27; --abbrev-commit
""");
    }

    [Fact]
    public void Alias_ShellCommand()
    {
        AssertHighlighter("gitconfig",
"""
[alias]
  blame-stats = !git log --pretty=format:'%an' | sort | uniq -c | sort -rn
""",
"""
<span class="hljs-section">[alias]</span>
  <span class="hljs-attr">blame-stats</span> = !git log --pretty=format:&#x27;%an&#x27; | sort | uniq -c | sort -rn
""");
    }

    [Fact]
    public void RemoteAndBranch_Origin()
    {
        AssertHighlighter("gitconfig",
"""
[remote "origin"]
  url = git@github.com:org/repo.git
  fetch = +refs/heads/*:refs/remotes/origin/*
""",
"""
<span class="hljs-section">[remote &quot;origin&quot;]</span>
  <span class="hljs-attr">url</span> = git@github.com:org/repo.git
  <span class="hljs-attr">fetch</span> = +refs/heads/*:refs/remotes/origin/*
""");
    }

    [Fact]
    public void RemoteAndBranch_BranchTrack()
    {
        AssertHighlighter("gitconfig",
"""
[branch "main"]
  remote = origin
  merge = refs/heads/main
""",
"""
<span class="hljs-section">[branch &quot;main&quot;]</span>
  <span class="hljs-attr">remote</span> = origin
  <span class="hljs-attr">merge</span> = refs/heads/main
""");
    }

    [Fact]
    public void RemoteAndBranch_BranchRebase()
    {
        AssertHighlighter("gitconfig",
"""
[branch "feature"]
  remote = origin
  merge = refs/heads/feature
  rebase = true
""",
"""
<span class="hljs-section">[branch &quot;feature&quot;]</span>
  <span class="hljs-attr">remote</span> = origin
  <span class="hljs-attr">merge</span> = refs/heads/feature
  <span class="hljs-attr">rebase</span> = true
""");
    }

    [Fact]
    public void RemoteAndBranch_PushDefault()
    {
        AssertHighlighter("gitconfig",
"""
[push]
  default = current
  autoSetupRemote = true
""",
"""
<span class="hljs-section">[push]</span>
  <span class="hljs-attr">default</span> = current
  <span class="hljs-attr">autoSetupRemote</span> = true
""");
    }

    [Fact]
    public void RemoteAndBranch_PullRebase()
    {
        AssertHighlighter("gitconfig",
"""
[pull]
  rebase = true
  ff = only
""",
"""
<span class="hljs-section">[pull]</span>
  <span class="hljs-attr">rebase</span> = true
  <span class="hljs-attr">ff</span> = only
""");
    }

    [Fact]
    public void MergeAndDiff_MergeTool()
    {
        AssertHighlighter("gitconfig",
"""
[merge]
  tool = vscode
  conflictStyle = zdiff3
""",
"""
<span class="hljs-section">[merge]</span>
  <span class="hljs-attr">tool</span> = vscode
  <span class="hljs-attr">conflictStyle</span> = zdiff3
""");
    }

    [Fact]
    public void MergeAndDiff_MergeToolNamed()
    {
        AssertHighlighter("gitconfig",
"""
[mergetool "vscode"]
  cmd = code --wait $MERGED
""",
"""
<span class="hljs-section">[mergetool &quot;vscode&quot;]</span>
  <span class="hljs-attr">cmd</span> = code --wait $MERGED
""");
    }

    [Fact]
    public void MergeAndDiff_DiffTool()
    {
        AssertHighlighter("gitconfig",
"""
[diff]
  tool = vscode
  renames = copies
""",
"""
<span class="hljs-section">[diff]</span>
  <span class="hljs-attr">tool</span> = vscode
  <span class="hljs-attr">renames</span> = copies
""");
    }

    [Fact]
    public void MergeAndDiff_DiffToolNamed()
    {
        AssertHighlighter("gitconfig",
"""
[difftool "vscode"]
  cmd = code --wait --diff $LOCAL $REMOTE
""",
"""
<span class="hljs-section">[difftool &quot;vscode&quot;]</span>
  <span class="hljs-attr">cmd</span> = code --wait --diff $LOCAL $REMOTE
""");
    }

    [Fact]
    public void ColorSettings_BasicEnable()
    {
        AssertHighlighter("gitconfig",
"""
[color]
  ui = auto
  status = auto
  diff = auto
  branch = auto
""",
"""
<span class="hljs-section">[color]</span>
  <span class="hljs-attr">ui</span> = auto
  <span class="hljs-attr">status</span> = auto
  <span class="hljs-attr">diff</span> = auto
  <span class="hljs-attr">branch</span> = auto
""");
    }

    [Fact]
    public void ColorSettings_StatusColors()
    {
        AssertHighlighter("gitconfig",
"""
[color "status"]
  added = green
  changed = yellow
  untracked = red
""",
"""
<span class="hljs-section">[color &quot;status&quot;]</span>
  <span class="hljs-attr">added</span> = green
  <span class="hljs-attr">changed</span> = yellow
  <span class="hljs-attr">untracked</span> = red
""");
    }

    [Fact]
    public void ColorSettings_BranchColors()
    {
        AssertHighlighter("gitconfig",
"""
[color "branch"]
  current = "bold green"
  remote = "yellow"
""",
"""
<span class="hljs-section">[color &quot;branch&quot;]</span>
  <span class="hljs-attr">current</span> = &quot;bold green&quot;
  <span class="hljs-attr">remote</span> = &quot;yellow&quot;
""");
    }

    [Fact]
    public void Url_InsteadOfSsh()
    {
        AssertHighlighter("gitconfig",
"""
[url "git@github.com:"]
  insteadOf = https://github.com/
""",
"""
<span class="hljs-section">[url &quot;git@github.com:&quot;]</span>
  <span class="hljs-attr">insteadOf</span> = https://github.com/
""");
    }

    [Fact]
    public void Url_PushInsteadOf()
    {
        AssertHighlighter("gitconfig",
"""
[url "git@github.com:"]
  pushInsteadOf = https://github.com/
""",
"""
<span class="hljs-section">[url &quot;git@github.com:&quot;]</span>
  <span class="hljs-attr">pushInsteadOf</span> = https://github.com/
""");
    }

    [Fact]
    public void Include_Plain()
    {
        AssertHighlighter("gitconfig",
"""
[include]
  path = ~/.gitconfig.shared
""",
"""
<span class="hljs-section">[include]</span>
  <span class="hljs-attr">path</span> = ~/.gitconfig.shared
""");
    }

    [Fact]
    public void Include_ConditionalGit()
    {
        AssertHighlighter("gitconfig",
"""
[includeIf "gitdir:~/work/"]
  path = ~/.gitconfig.work
""",
"""
<span class="hljs-section">[includeIf &quot;gitdir:~/work/&quot;]</span>
  <span class="hljs-attr">path</span> = ~/.gitconfig.work
""");
    }

    [Fact]
    public void Include_OnBranch()
    {
        AssertHighlighter("gitconfig",
"""
[includeIf "onbranch:main"]
  path = ~/.gitconfig.main
""",
"""
<span class="hljs-section">[includeIf &quot;onbranch:main&quot;]</span>
  <span class="hljs-attr">path</span> = ~/.gitconfig.main
""");
    }

    [Fact]
    public void CredentialAndSecurity_CredentialHelper()
    {
        AssertHighlighter("gitconfig",
"""
[credential]
  helper = osxkeychain
""",
"""
<span class="hljs-section">[credential]</span>
  <span class="hljs-attr">helper</span> = osxkeychain
""");
    }

    [Fact]
    public void CredentialAndSecurity_CredentialHelperHost()
    {
        AssertHighlighter("gitconfig",
"""
[credential "https://github.com"]
  helper = !gh auth git-credential
""",
"""
<span class="hljs-section">[credential &quot;https://github.com&quot;]</span>
  <span class="hljs-attr">helper</span> = !gh auth git-credential
""");
    }

    [Fact]
    public void CredentialAndSecurity_GpgSign()
    {
        AssertHighlighter("gitconfig",
"""
[commit]
  gpgSign = true

[tag]
  gpgSign = true
""",
"""
<span class="hljs-section">[commit]</span>
  <span class="hljs-attr">gpgSign</span> = true

<span class="hljs-section">[tag]</span>
  <span class="hljs-attr">gpgSign</span> = true
""");
    }

    [Fact]
    public void CredentialAndSecurity_GpgProgram()
    {
        AssertHighlighter("gitconfig",
"""
[gpg]
  program = gpg
""",
"""
<span class="hljs-section">[gpg]</span>
  <span class="hljs-attr">program</span> = gpg
""");
    }

    [Fact]
    public void FilterAndRerere_Lfs()
    {
        AssertHighlighter("gitconfig",
"""
[filter "lfs"]
  clean = git-lfs clean -- %f
  smudge = git-lfs smudge -- %f
  process = git-lfs filter-process
  required = true
""",
"""
<span class="hljs-section">[filter &quot;lfs&quot;]</span>
  <span class="hljs-attr">clean</span> = git-lfs clean -- %f
  <span class="hljs-attr">smudge</span> = git-lfs smudge -- %f
  <span class="hljs-attr">process</span> = git-lfs filter-process
  <span class="hljs-attr">required</span> = true
""");
    }

    [Fact]
    public void FilterAndRerere_Rerere()
    {
        AssertHighlighter("gitconfig",
"""
[rerere]
  enabled = true
  autoupdate = true
""",
"""
<span class="hljs-section">[rerere]</span>
  <span class="hljs-attr">enabled</span> = true
  <span class="hljs-attr">autoupdate</span> = true
""");
    }

    [Fact]
    public void BooleanFormats_True()
    {
        AssertHighlighter("gitconfig",
"""
[core]
  autocrlf = true
""",
"""
<span class="hljs-section">[core]</span>
  <span class="hljs-attr">autocrlf</span> = true
""");
    }

    [Fact]
    public void BooleanFormats_False()
    {
        AssertHighlighter("gitconfig",
"""
[core]
  fileMode = false
""",
"""
<span class="hljs-section">[core]</span>
  <span class="hljs-attr">fileMode</span> = false
""");
    }

    [Fact]
    public void BooleanFormats_Yes()
    {
        AssertHighlighter("gitconfig",
"""
[push]
  autoSetupRemote = yes
""",
"""
<span class="hljs-section">[push]</span>
  <span class="hljs-attr">autoSetupRemote</span> = yes
""");
    }

    [Fact]
    public void BooleanFormats_On()
    {
        AssertHighlighter("gitconfig",
"""
[color]
  ui = on
""",
"""
<span class="hljs-section">[color]</span>
  <span class="hljs-attr">ui</span> = on
""");
    }

    [Fact]
    public void BooleanFormats_NumericTrue()
    {
        AssertHighlighter("gitconfig",
"""
[core]
  bare = 1
""",
"""
<span class="hljs-section">[core]</span>
  <span class="hljs-attr">bare</span> = 1
""");
    }

    [Fact]
    public void Comment_Hash()
    {
        AssertHighlighter("gitconfig",
"""
# this is a comment
""",
"""
<span class="hljs-comment"># this is a comment</span>
""");
    }

    [Fact]
    public void Comment_Semicolon()
    {
        AssertHighlighter("gitconfig",
"""
; alternative comment
""",
"""
<span class="hljs-comment">; alternative comment</span>
""");
    }

    [Fact]
    public void Comment_AboveSetting()
    {
        AssertHighlighter("gitconfig",
"""
# default editor
[core]
  editor = vim
""",
"""
<span class="hljs-comment"># default editor</span>
<span class="hljs-section">[core]</span>
  <span class="hljs-attr">editor</span> = vim
""");
    }

    [Fact]
    public void Submodule_GitmodulesEntry()
    {
        AssertHighlighter("gitconfig",
"""
[submodule "vendor/lib"]
  path = vendor/lib
  url = https://github.com/org/lib.git
  branch = main
""",
"""
<span class="hljs-section">[submodule &quot;vendor/lib&quot;]</span>
  <span class="hljs-attr">path</span> = vendor/lib
  <span class="hljs-attr">url</span> = https://github.com/org/lib.git
  <span class="hljs-attr">branch</span> = main
""");
    }

    [Fact]
    public void Composite_PersonalConfig()
    {
        AssertHighlighter("gitconfig",
"""
[user]
  name = Alice Example
  email = alice@example.com
  signingKey = ABC123DEF456

[core]
  autocrlf = input
  editor = vim
  pager = less -FRX
  excludesFile = ~/.gitignore_global

[init]
  defaultBranch = main

[push]
  default = current
  autoSetupRemote = true

[pull]
  rebase = true
  ff = only

[fetch]
  prune = true

[rerere]
  enabled = true

[commit]
  gpgSign = true
""",
"""
<span class="hljs-section">[user]</span>
  <span class="hljs-attr">name</span> = Alice Example
  <span class="hljs-attr">email</span> = alice@example.com
  <span class="hljs-attr">signingKey</span> = ABC123DEF456

<span class="hljs-section">[core]</span>
  <span class="hljs-attr">autocrlf</span> = input
  <span class="hljs-attr">editor</span> = vim
  <span class="hljs-attr">pager</span> = less -FRX
  <span class="hljs-attr">excludesFile</span> = ~/.gitignore_global

<span class="hljs-section">[init]</span>
  <span class="hljs-attr">defaultBranch</span> = main

<span class="hljs-section">[push]</span>
  <span class="hljs-attr">default</span> = current
  <span class="hljs-attr">autoSetupRemote</span> = true

<span class="hljs-section">[pull]</span>
  <span class="hljs-attr">rebase</span> = true
  <span class="hljs-attr">ff</span> = only

<span class="hljs-section">[fetch]</span>
  <span class="hljs-attr">prune</span> = true

<span class="hljs-section">[rerere]</span>
  <span class="hljs-attr">enabled</span> = true

<span class="hljs-section">[commit]</span>
  <span class="hljs-attr">gpgSign</span> = true
""");
    }

    [Fact]
    public void Composite_AliasesConfig()
    {
        AssertHighlighter("gitconfig",
"""
[alias]
  st = status -sb
  co = checkout
  br = branch
  ci = commit
  amend = commit --amend --no-edit
  unstage = reset HEAD --
  last = log -1 HEAD
  lg = log --graph --pretty=format:'%C(yellow)%h%Creset %s %C(cyan)(%an, %ar)%Creset' --abbrev-commit
  cleanup = !git branch --merged | grep -v '\*\|main\|master' | xargs -n 1 git branch -d
""",
"""
<span class="hljs-section">[alias]</span>
  <span class="hljs-attr">st</span> = status -sb
  <span class="hljs-attr">co</span> = checkout
  <span class="hljs-attr">br</span> = branch
  <span class="hljs-attr">ci</span> = commit
  <span class="hljs-attr">amend</span> = commit --amend --no-edit
  <span class="hljs-attr">unstage</span> = reset HEAD --
  <span class="hljs-attr">last</span> = log -1 HEAD
  <span class="hljs-attr">lg</span> = log --graph --pretty=format:&#x27;%C(yellow)%h%Creset %s %C(cyan)(%an, %ar)%Creset&#x27; --abbrev-commit
  <span class="hljs-attr">cleanup</span> = !git branch --merged | grep -v &#x27;\*\|main\|master&#x27; | xargs -n 1 git branch -d
""");
    }

    [Fact]
    public void Composite_WorkSplit()
    {
        AssertHighlighter("gitconfig",
"""
# global ~/.gitconfig
[includeIf "gitdir:~/work/"]
  path = ~/.gitconfig.work

[includeIf "gitdir:~/personal/"]
  path = ~/.gitconfig.personal
""",
"""
<span class="hljs-comment"># global ~/.gitconfig</span>
<span class="hljs-section">[includeIf &quot;gitdir:~/work/&quot;]</span>
  <span class="hljs-attr">path</span> = ~/.gitconfig.work

<span class="hljs-section">[includeIf &quot;gitdir:~/personal/&quot;]</span>
  <span class="hljs-attr">path</span> = ~/.gitconfig.personal
""");
    }

    [Fact]
    public void SpecialEdge_Empty()
    {
        AssertHighlighter("gitconfig",
"""

""",
"""

""");
    }

    [Fact]
    public void SpecialEdge_OnlyComment()
    {
        AssertHighlighter("gitconfig",
"""
# nothing here
""",
"""
<span class="hljs-comment"># nothing here</span>
""");
    }

    [Fact]
    public void SpecialEdge_BlankLineBetween()
    {
        AssertHighlighter("gitconfig",
"""
[core]
  editor = vim

[user]
  name = Alice
""",
"""
<span class="hljs-section">[core]</span>
  <span class="hljs-attr">editor</span> = vim

<span class="hljs-section">[user]</span>
  <span class="hljs-attr">name</span> = Alice
""");
    }
}
