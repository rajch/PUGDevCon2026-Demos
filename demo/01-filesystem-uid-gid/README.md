# Demo Scenario 1: UID/GID & Filesystem Permissions

## The Context

Modern .NET container images (`mcr.microsoft.com/dotnet/aspnet:10.0`) follow container security best practices by defaulting to a non-root user:
* **Username**: `app`
* **UID**: `1654`
* **GID**: `1654`

Inside the container image, `/app` and `/app/data` are owned by `1654:1654`. However, in production or local development, persistent storage (uploads, generated payslips) is typically mounted from a host folder or network volume.

---

## Step 1: The Failure (Naive Volume Mounting)

When we mount a host directory to `/app/data`, Docker replaces the container's internal directory with the host directory. If the host folder is owned by `root:root (0:0)` or another host UID with `0755` permissions, the non-root container process cannot write to it.

### Commands to Run:
```bash
# 1. Create a host folder owned by root (simulating a standard Docker or root-provisioned volume)
sudo mkdir -p ./naive-host-data && sudo chown 0:0 ./naive-host-data && sudo chmod 755 ./naive-host-data

# 2. Run the naive configuration
docker compose -f demo/01-filesystem-uid-gid/docker-compose.naive.yml up -d
```

### What to Observe:
1. Open `http://localhost:8080` in your browser.
2. Sign in as `alice@paydesk.local` (Password: `User@123!`).
3. Attempt to upload `samples/expenses_alice_2026_11.csv`.
4. **Crash / Error**: The upload fails with:
   ```text
   System.UnauthorizedAccessException: Access to the path '/app/data/uploads/2026-11' is denied.
   ```
5. Inspect the container user:
   ```bash
   docker exec paydesk-web-s1-naive id
   # uid=1654(app) gid=1654(app) groups=1654(app)
   ```

---

## Step 2: The Insecure Anti-Pattern ("Just Make It Root")

When confronted with permission errors, teams under delivery pressure often apply a naive "fix": they switch the container to run as `root` (`user: "0:0"` or removing `USER app`).

### Commands to Run:
```bash
docker compose -f demo/01-filesystem-uid-gid/docker-compose.naive.yml down
docker compose -f demo/01-filesystem-uid-gid/docker-compose.insecure-fix.yml up -d
```

### What to Observe:
1. Try uploading `samples/expenses_alice_2026_11.csv` again.
2. The upload now succeeds!
3. **The Security Risk**: Check the container identity:
   ```bash
   docker exec paydesk-web-s1-insecure id
   # uid=0(root) gid=0(root) groups=0(root)
   ```
4. Check the file created on the host:
   ```bash
   ls -la ./naive-host-data/uploads/2026-11/
   # Files are owned by root:root on the host system!
   ```
5. **Session Talking Point**: Running as `root` grants unnecessary capabilities, allows reading sensitive container files, and significantly increases the blast radius of any remote code execution or container breakout vulnerability.

---

## Step 3: The Proper Corrections

### Proper Docker Fix: Volume Initialization / Ownership
Instead of elevating the container to root, ensure the volume is owned by the app UID (`1654:1654`):

```bash
docker compose -f demo/01-filesystem-uid-gid/docker-compose.insecure-fix.yml down

# Run proper fix using a volume-init container or chown:
docker compose -f demo/01-filesystem-uid-gid/docker-compose.proper-fix.yml up -d
```
The app continues running safely as `app (1654:1654)` and writes without issues.

### Proper Kubernetes Fix: `securityContext.fsGroup`
In Kubernetes, you do not need init containers or host `chmod` hacks. Kubelet automatically handles volume permissions using `fsGroup`:

```yaml
spec:
  securityContext:
    runAsNonRoot: true
    runAsUser: 1654
    runAsGroup: 1654
    fsGroup: 1654          # <-- Kubelet mounts volume with GID 1654 and group-write permissions!
```
Review the complete Kubernetes manifest in [k8s-manifest.yaml](file:///home/rajch/projects/rajch/PUGDevCon2026-Demos/demo/01-filesystem-uid-gid/k8s-manifest.yaml).

### Cleanup
```bash
docker compose -f demo/01-filesystem-uid-gid/docker-compose.proper-fix.yml down
```
