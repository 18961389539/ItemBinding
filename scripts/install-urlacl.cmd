@echo off
REM ============================================================
REM  Install URL ACLs for MainAPP HttpListener endpoints.
REM  CameraWebHost : http://+:5188/  (remote camera view)
REM  AiWebHost     : http://+:5190/  (remote AI chat panel)
REM  OpsWebHost    : http://+:5191/  (unified ops portal)
REM
REM  Run this script ONCE per machine as Administrator.
REM  Without the ACLs these listeners silently fall back to
REM  http://127.0.0.1/ (localhost only, phones cannot connect).
REM
REM  2026-09-16: 5191 (OpsWebHost) was missing here and in the
REM  deployment doc, so the ops portal always bound to loopback
REM  on sites that followed that doc. Keep the two in sync.
REM ============================================================
netsh http add urlacl url=http://+:5188/ user=Everyone
netsh http add urlacl url=http://+:5190/ user=Everyone
netsh http add urlacl url=http://+:5191/ user=Everyone
echo Done. Verify with: netsh http show urlacl
echo Expected: three entries (5188 / 5190 / 5191).
pause
