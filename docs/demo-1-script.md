# Demo Scenario 1: UID/GID & Filesystem Permissions

## The Context

Modern .NET container images (`mcr.microsoft.com/dotnet/aspnet:10.0`) follow container security best practices by defaulting to a non-root user:
* **Username**: `app`
* **UID**: `1654`
* **GID**: `1654`

They also do not include many operating system libraries and utilities, to reduce the attack surface. To ensure that our application works, we need to ensure that the user that it runs as has appropriate permissions in the right places, and also that our final image contains any additional dependencies that we need.

The Paydesk application consists of:
  * Paydesk Web: An ASP.NET Core application that allows users to upload expenses and download payslips. It also invokes Consolidator when an admin-level user requests it. It also allows the user to check the status of the backend by pinging it.
  * Consolidator: A .NET console application, which reads uploaded expenses, consolidates them and generates PDF payslips. It uses the PDFSharp library.

Inside the container image, the directory layout is as follows:

```
/app
|--/web
|--/consolidator
|--/data
   |--/uploads
   |--/payslips
```

Expense files are written by PayDesk Web to `/data/uploads`. Payslip files are written by Consolidator to `/data/payslips`. For proper container use, a volume needs to be mounted on `/data`, and the app user needs write permissions.

## Step 1: The Failure (Naive Volume Mounting)

We have built a Dockerfile following Microsoft's basic guidance, in [package/paydesk-0.1.0-beta1.dockerfile](../package/paydesk-0.1.0-beta1.dockerfile). We have also built a compose manifest in [deploy/d1-1.docker-compose.yml](../deploy/d1-1.docker-compose.yml), which creates a container from the image built from that Dockerfile, and mounts a volume to the `/app/data` path.

### Commands to run

```bash
docker compose -f deploy/d1-1.docker-compose.yml up -d
```
This brings up the application successfully. It can be accessed by browsing [http://localhost:8080](http://localhost:8080).

### What to Observe:
1. Open `http://localhost:8080` in your browser.
2. Sign in as `alice@paydesk.local` (Password: `User@123!`).
3. Attempt to upload `samples/expenses_alice_2026_11.csv`.
4. **Crash / Error**: The upload fails with:
   ```text
   System.UnauthorizedAccessException: Access to the path '/app/data/uploads/2026-11' is denied.
   ```
5. Also browse the Status page. Note that the Ping functionality fails, with the error: `PlatformNotSupportedException: The system's ping utility could not be found.` .Net on Linux, when run as a Nonroot user uses the operating system's ping utility to perform the operation. The base image does not include that utility.
---
## Step 2: The Insecure Anti-Pattern ("Just Make It Root")

When confronted with permission errors, teams under delivery pressure often apply a naive "fix": they switch the container to run as `root` (`user: "0:0"` or removing `USER app` from the Dockerfile).

In our case, we have built an updated compose manifest in [deploy/d1-2.docker-compose.yml](../deploy/d1-2.docker-compose.yml), where we use the same image, but the container is set to run as root.

### Commands to Run:
```bash
docker compose -f deploy/d1-1.docker-compose.yml down --volumes
docker compose -f deploy/d1-2.docker-compose.yml up -d
```

### What to Observe:
1. Try uploading `samples/expenses_alice_2026_11.csv` again.
2. The upload now succeeds!
3. **The Security Risk**: Check the container identity:
   ```bash
   docker exec d1-2-paydesk-web id
   # uid=0(root) gid=0(root) groups=0(root)
   ```
4. Check the file created on the volume:
   ```bash
   docker exec d1-2-paydesk-web ls -la /app/data/uploads/2026-11/
   # Files are owned by root:root!
   ```
Running as `root` grants unnecessary capabilities, allows reading sensitive container files, and significantly increases the blast radius of any remote code execution or container breakout vulnerability.

Note that the Status page works fine, despite the image being the same. .Net on Linux can ping directly without using the operating system's ping utility if run as root.

Now, log in as `admin@paydesk.local`, and try to run the consolidation process. Note that it fails due to dependencies not being present with the error:
```text
[Error] Consolidation failed: No Fonts installed on this device!
```
---
## Step 3: The Proper Corrections

Instead of elevating the container permissions the correct way is to ensure that the Nonroot user has appropriate permissions on the volume. How to do it differs depending on the container management system. One way is to create the mount path (in this case `/app/data`) in the image with the correct permissions. The mounted volume then copies those permissions. This works in Docker.

We have built a Dockerfile following Microsoft's basic guidance, in [package/paydesk-0.1.0-beta2.dockerfile](../package/paydesk-0.1.0-beta2.dockerfile) which does this. We have also included the dependencies of our application which is basic font support and the OS ping utility.

We have also built a compose manifest in [deploy/d1-3.docker-compose.yml](../deploy/d1-3.docker-compose.yml), which uses the image built from the Dockerfile but does not elevate permissions in any way.

### Commands to Run:
```bash
docker compose -f deploy/d1-2.docker-compose.yml down --volumes
docker compose -f deploy/d1-3.docker-compose.yml up -d
```
### What to Observe:
1. Try uploading `samples/expenses_alice_2026_11.csv` again. The upload now succeeds!
2. Check the container identity:
   ```bash
   docker exec d1-3-paydesk-web id
   # uid=1654(app) gid=1654(app) groups=1654(app)
   ```
4. Check the file created on the volume:
   ```bash
   docker exec d1-3-paydesk-web ls -la /app/data/uploads/2026-11/
   # Files are owned by app:app
   ```

Note that the Status page works fine, even while running as app. The image now includes the OS ping utility.

Now, log in as `admin@paydesk.local`, and run the consolidation process. There should be no errors now.

## Cleaning Up

Run the following command:

```bash
docker compose -f deploy/d1-3.docker-compose.yml down --volumes
```

---






