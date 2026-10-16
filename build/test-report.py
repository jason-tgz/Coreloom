#!/usr/bin/env python3
"""Sums up test runs (.trx files) for GitHub Actions: a notice per test project with its counts and skipped tests, and
an error annotation per failed test with its message and the top of its stack, so a failure can be read on the run's
summary page without the log. The Windows build does the same in build.ps1.

Usage: python3 build/test-report.py artifacts/test-results
"""

import pathlib
import sys
import xml.etree.ElementTree as ET

NS = {"t": "http://microsoft.com/schemas/VisualStudio/TeamTest/2010"}


def escape(text: str, prop: bool = False) -> str:
    text = text.replace("%", "%25").replace("\r", "%0D").replace("\n", "%0A")
    if prop:
        text = text.replace(":", "%3A").replace(",", "%2C")
    return text


def report(path: pathlib.Path) -> None:
    root = ET.parse(path).getroot()
    method = root.find(".//t:UnitTest/t:TestMethod", NS)
    project = pathlib.PureWindowsPath(method.get("codeBase", "")).stem if method is not None else path.name
    summary = root.find(".//t:ResultSummary", NS)
    counters = summary.find("t:Counters", NS) if summary is not None else None
    results = root.findall(".//t:UnitTestResult", NS)
    skipped = [".".join(r.get("testName", "").split(".")[-2:]) for r in results if r.get("outcome") == "NotExecuted"]
    line = (f"{project} ({summary.get('outcome') if summary is not None else '?'}): "
            f"{counters.get('passed') if counters is not None else '?'} passed, "
            f"{counters.get('failed') if counters is not None else '?'} failed, {len(skipped)} skipped")
    if skipped:
        line += " - skipped: " + "; ".join(skipped[:12])
    print(f"::notice title=Tests::{escape(line)}")
    for result in results:
        if result.get("outcome") != "Failed":
            continue
        message = result.find("t:Output/t:ErrorInfo/t:Message", NS)
        stack = result.find("t:Output/t:ErrorInfo/t:StackTrace", NS)
        text = (message.text or "").strip() if message is not None else "(no message)"
        if stack is not None and stack.text:
            text += "\n" + "\n".join(stack.text.strip().splitlines()[:4])
        title = escape(f"Test failed: {result.get('testName', '?')}", prop=True)
        print(f"::error title={title}::{escape(text)}")


def main() -> int:
    folder = pathlib.Path(sys.argv[1] if len(sys.argv) > 1 else "artifacts/test-results")
    files = sorted(folder.rglob("*.trx")) if folder.exists() else []
    if not files:
        print(f"::warning title=Tests::{escape(f'No test results (.trx) in {folder}')}")
    for path in files:
        try:
            report(path)
        except (ET.ParseError, OSError) as error:
            print(f"::warning title=Tests::{escape(f'Could not read {path.name}: {error}')}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
