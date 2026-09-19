"""通过 GitHub REST API 推送本地 HEAD（支持**多个**未推送提交），并把本地 ref 对齐到远端生成的提交对象。

与 gh_api_push3.py 的区别：v3 只支持"HEAD 的父提交 == 远端 head"这单个提交的情形；
本次本地领先远端 2 个提交，故 v4 逐提交构造远端对象（每个提交：blob → tree → commit），
并对**每个**提交校验新建 tree 的 sha 与本地 `commit^{tree}` 逐字节一致。

背景：github.com:443 在本网络下对 git 不可用（直连 schannel 握手失败；经代理 CONNECT 502），
但 api.github.com 可达。凭据从 GCM 读取（注意：系统配的 credential.helper=helper-selector 在非交互
环境下会挂住，必须显式用 -c credential.helper=manager）。

安全：① 每个提交更新前校验 tree sha；② 任一提交反推不出远端字节即中止（此时远端 ref 未动，内容未变）。
对齐：GitHub 生成 commit 对象与本地存在字节差异（实测为 message 末尾换行被去掉）。
本脚本按「远端 ref 的父链」重建对象：父 = 上一个远端 sha（而非本地 sha），因此对齐后的对象天然串成一条链。
"""

import base64
import json
import os
import subprocess
import sys
import urllib.error
import urllib.request

CWD = r"D:\ItemBinding"
OWNER_REPO = "18961389539/ItemBinding"
API = "https://api.github.com"
HEADERS = {
    "Accept": "application/vnd.github+json",
    "X-GitHub-Api-Version": "2022-11-28",
    "User-Agent": "itembinding-api-push",
}


def sh(args, input_bytes=None):
    return subprocess.run(args, cwd=CWD, capture_output=True, input=input_bytes)


def sh_text(args):
    p = sh(args)
    if p.returncode != 0:
        raise SystemExit(f"命令失败 {' '.join(args)}: {p.stderr.decode('utf-8', 'replace')}")
    return p.stdout.decode("utf-8", "replace").strip()


def hash_object(data, write=False):
    args = ["git", "hash-object", "-t", "commit"]
    if write:
        args.append("-w")
    args.append("--stdin")
    p = subprocess.run(args, cwd=CWD, capture_output=True, input=data)
    if p.returncode != 0:
        raise SystemExit(p.stderr.decode("utf-8", "replace"))
    return p.stdout.decode().strip()


def get_token():
    """读 GCM 凭据。显式使用 manager helper——系统配置的 helper-selector 在非交互下会挂起。
    若已通过环境变量 GITHUB_TOKEN 注入，则优先使用（避免 GCM 偶发挂起阻塞推送）。"""
    env_token = os.environ.get("GITHUB_TOKEN")
    if env_token:
        return env_token
    env = dict(os.environ)
    env["GIT_TERMINAL_PROMPT"] = "0"
    env["GCM_INTERACTIVE"] = "never"
    for helper in ("manager", None):
        args = ["git"]
        if helper:
            args += ["-c", f"credential.helper={helper}"]
        args += ["credential", "fill"]
        try:
            p = subprocess.run(args, cwd=CWD, input=b"protocol=https\nhost=github.com\n\n",
                               capture_output=True, env=env, timeout=30)
        except subprocess.TimeoutExpired:
            continue
        for line in p.stdout.decode("utf-8", "replace").splitlines():
            if line.startswith("password="):
                return line[len("password="):].strip()
    return None


def api(token, method, path, payload=None):
    data = json.dumps(payload).encode("utf-8") if payload is not None else None
    req = urllib.request.Request(API + path, data=data, method=method)
    for k, v in HEADERS.items():
        req.add_header(k, v)
    req.add_header("Authorization", f"Bearer {token}")
    if data is not None:
        req.add_header("Content-Type", "application/json")
    try:
        with urllib.request.urlopen(req, timeout=90) as resp:
            body = resp.read().decode("utf-8")
            return resp.status, (json.loads(body) if body else None)
    except urllib.error.HTTPError as e:
        return e.code, {"error": e.read().decode("utf-8", "replace")}
    except Exception as e:  # 网络异常
        return 0, {"error": repr(e)}


def commit_meta(sha):
    g = lambda fmt: sh_text(["git", "log", "-1", f"--format={fmt}", sha])
    raw = lambda fmt: sh_text(["git", "log", "-1", "--date=raw", f"--format={fmt}", sha])
    return {
        "message": sh_text(["git", "log", "-1", "--format=%B", sha]).rstrip("\n"),
        # GitHub API 要 ISO 8601
        "an": g("%an"), "ae": g("%ae"), "ad": g("%aI"),
        "cn": g("%cn"), "ce": g("%ce"), "cd": g("%cI"),
        # commit 对象内部要 "unix时间戳 +时区"（raw），用 %aI 会写出非法日期（fsck: badDate）
        "ad_raw": raw("%ad"), "cd_raw": raw("%cd"),
    }


def build_header(tree_sha, parent_sha, meta):
    return (
        f"tree {tree_sha}\n"
        f"parent {parent_sha}\n"
        f"author {meta['an']} <{meta['ae']}> {meta['ad_raw']}\n"
        f"committer {meta['cn']} <{meta['ce']}> {meta['cd_raw']}\n"
        f"\n"
    ).encode("utf-8")


def main():
    head = sh_text(["git", "rev-parse", "HEAD"])
    print(f"本地 HEAD        : {head[:12]}")

    token = get_token()
    if not token:
        print("❌ 未取到凭据：GCM 里没有可用的 github 凭据")
        return 1
    print(f"✅ 已取得凭据（长度 {len(token)}，内容不打印）")

    status, ref = api(token, "GET", f"/repos/{OWNER_REPO}/git/ref/heads/main")
    if status != 200:
        print(f"❌ 读取远端 ref 失败：HTTP {status} {ref}")
        return 1
    remote_sha = ref["object"]["sha"]
    print(f"远端 main        : {remote_sha[:12]}")

    # 快进关系校验：远端必须是本地 HEAD 的祖先，否则拒绝（防覆盖他人提交）
    if sh(["git", "merge-base", "--is-ancestor", remote_sha, head]).returncode != 0:
        print("❌ 远端 main 不是本地 HEAD 的祖先（远端有本地没有的提交），中止")
        return 1
    print("✅ 快进关系成立（远端是本地祖先）")

    commits = sh_text(["git", "rev-list", "--reverse", f"{remote_sha}..HEAD"]).split()
    if not commits:
        print("✅ 没有需要推送的提交")
        return 0
    print(f"待推送提交       : {len(commits)} 个")
    for c in commits:
        print(f"   - {c[:12]} {sh_text(['git', 'log', '-1', '--format=%s', c])[:60]}")

    status, parent = api(token, "GET", f"/repos/{OWNER_REPO}/git/commits/{remote_sha}")
    if status != 200:
        print(f"❌ 读取远端父提交失败：HTTP {status} {parent}")
        return 1
    base_tree = parent["tree"]["sha"]

    parent_local = remote_sha
    parent_remote = remote_sha

    for idx, c in enumerate(commits, 1):
        local_tree = sh_text(["git", "rev-parse", f"{c}^{{tree}}"])
        print(f"\n[{idx}/{len(commits)}] {c[:12]} tree={local_tree[:12]}")

        raw = sh(["git", "diff", "--name-status", "--no-renames", "-z", f"{parent_local}..{c}"]).stdout
        parts = [p for p in raw.split(b"\x00") if p]
        entries = [(parts[i].decode("ascii", "replace")[0], parts[i + 1].decode("utf-8", "replace"))
                   for i in range(0, len(parts), 2)]
        print(f"    变更文件: {len(entries)}")

        tree_entries = []
        for st, path in entries:
            if st == "D":
                tree_entries.append({"path": path, "mode": "100644", "type": "blob", "sha": None})
                continue
            blob_bytes = sh(["git", "cat-file", "blob", f"{c}:{path}"]).stdout
            mode = sh_text(["git", "ls-tree", c, "--", path]).split()[0]
            code, blob = api(token, "POST", f"/repos/{OWNER_REPO}/git/blobs",
                             {"content": base64.b64encode(blob_bytes).decode("ascii"), "encoding": "base64"})
            if code not in (200, 201):
                print(f"❌ 创建 blob 失败：{path} → HTTP {code} {blob}")
                return 1
            tree_entries.append({"path": path, "mode": mode, "type": "blob", "sha": blob["sha"]})

        code, tree = api(token, "POST", f"/repos/{OWNER_REPO}/git/trees",
                         {"base_tree": base_tree, "tree": tree_entries})
        if code not in (200, 201):
            print(f"❌ 创建 tree 失败：HTTP {code} {tree}")
            return 1
        new_tree = tree["sha"]
        if new_tree != local_tree:
            print(f"❌ tree sha 与本地不一致（远端 {new_tree[:12]} vs 本地 {local_tree[:12]}），中止；远端 ref 未动")
            return 1
        print(f"    ✅ tree 校验一致（{new_tree[:12]}）")

        meta = commit_meta(c)
        code, commit = api(token, "POST", f"/repos/{OWNER_REPO}/git/commits", {
            "message": meta["message"], "tree": new_tree, "parents": [parent_remote],
            "author": {"name": meta["an"], "email": meta["ae"], "date": meta["ad"]},
            "committer": {"name": meta["cn"], "email": meta["ce"], "date": meta["cd"]},
        })
        if code not in (200, 201):
            print(f"❌ 创建 commit 失败：HTTP {code} {commit}")
            return 1
        remote_commit = commit["sha"]

        # ── 反推远端提交的字节（GitHub 会去掉 message 末尾换行），使本地能重建同一对象 ──
        header = build_header(new_tree, parent_remote, meta)
        body = meta["message"].encode("utf-8")
        candidates = {
            "原样": body,
            "去末尾换行": body.rstrip(b"\n"),
            "补单个换行": body.rstrip(b"\n") + b"\n",
        }
        matched = None
        for label, cand in candidates.items():
            if hash_object(header + cand) == remote_commit:
                matched = (label, cand)
                break
        if matched is None:
            print("❌ 未能反推出远端提交字节（本地与 GitHub 的对象字节差异不是已知形态）；"
                  "远端 ref 未更新，内容未被改变。可改用 git fetch + reset 对齐。")
            return 1
        label, cand = matched
        written = hash_object(header + cand, write=True)
        if written != remote_commit:
            print(f"❌ 写入本地对象 sha 不一致：{written}")
            return 1
        print(f"    ✅ 已反推[{label}]并对齐为 {remote_commit[:12]}")

        parent_local = c
        parent_remote = remote_commit
        base_tree = new_tree

    # ── 更新远端 ref ──
    code, updated = api(token, "PATCH", f"/repos/{OWNER_REPO}/git/refs/heads/main",
                        {"sha": parent_remote, "force": False})
    if code not in (200, 201):
        print(f"❌ 更新远端 ref 失败：HTTP {code} {updated}")
        return 1
    print(f"\n🎉 远端 main: {remote_sha[:12]} → {parent_remote[:12]}")

    # ── 本地 ref 对齐（update-ref 在本环境会静默失败，直接写引用文件）──
    subprocess.run(["git", "tag", "-f", "pre-api-push-4", head], cwd=CWD, capture_output=True)
    subprocess.run(["git", "update-ref", "refs/heads/main", parent_remote], cwd=CWD, capture_output=True)
    for rel in (("refs", "heads", "main"), ("refs", "remotes", "origin", "main")):
        path = os.path.join(CWD, ".git", *rel)
        os.makedirs(os.path.dirname(path), exist_ok=True)
        with open(path, "w", encoding="ascii") as f:
            f.write(parent_remote + "\n")
    print(f"✅ 本地 main 与 origin/main 均已对齐到 {parent_remote[:12]}")
    print(f"   旧 HEAD 保留在 tag pre-api-push-4（{head[:12]}）")
    return 0


if __name__ == "__main__":
    sys.exit(main())
