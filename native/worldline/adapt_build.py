"""为冻结依赖补充显式 C++ 规则加载，不修改 C/C++ 源码。"""
from pathlib import Path
import re


def adapt(content):
    text = content.decode('utf-8')
    for old, new in (('@com_google_absl//', '@absl//'),
                     ('@com_google_googletest//', '@gtest//'),
                     ('@com_googlesource_code_re2//', '@re2//')):
        text = text.replace(old, new)
    rules = sorted(set(re.findall(r'(?m)^\s*(?:native\.)?(cc_\w+)\(', text)))
    imports = '\n'.join(re.findall(r'load\(.*?\)', text, re.DOTALL))
    loads = []
    for rule in rules:
        text = text.replace('native.' + rule + '(', rule + '(')
        if not re.search(r'[\"\x27]' + rule + r'[\"\x27]', imports):
            loads.append(f'load("@rules_cc//cc:{rule}.bzl", "{rule}")\n')
    return (''.join(loads) + text).encode('utf-8')


if __name__ == '__main__':
    for path in sorted(Path.cwd().rglob('*')):
        if path.is_file() and (path.name in ('BUILD', 'BUILD.bazel') or path.suffix == '.bzl'):
            content = path.read_bytes()
            updated = adapt(content)
            if updated != content:
                path.write_bytes(updated)
