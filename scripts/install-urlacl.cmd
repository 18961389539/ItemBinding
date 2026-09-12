@echo off
REM ============================================================
REM  Install URL ACLs for MainAPP HttpListener endpoints.
REM  CameraWebHost : http://+:5188/  (remote camera view)
REM  AiWebHost     : http://+:5190/  (remote AI chat panel)
REM
REM  Run this script ONCE per machine as Administrator.
REM  Without the ACLs both listeners silently fall back to
REM  http://127.0.0.1/ (localhost only, phones cannot connect).
REM ============================================================
netsh http add urlacl url=http://+:5188/ user=Everyone
netsh http add urlacl url=http://+:5190/ user=Everyone
echo Done. Verify with: netsh http show urlacl
pause
