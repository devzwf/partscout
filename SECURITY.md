# Security

PartScout stores supplier API keys (encrypted with the master key from `.env`) and account passwords (hashed). It is meant for a home network or a private VPN, **not for exposure to the Internet**.

## Reporting a problem
Please do not open a public issue. Use GitHub's private reporting instead: **Security** tab → **Report a vulnerability**. If that is not possible, write to devzwf@proton.me.

Include the version, what an attacker could do, and how to reproduce it. You will get an answer as soon as possible; this is a spare-time project, so please allow a few days.

## Supported versions
Only the latest release receives fixes.

## Good practice
- Keep `.env` private (`chmod 600`) and back it up: without the master key, the stored API keys cannot be read.
- Use HTTPS as soon as anyone else signs in, or passwords and keys travel in clear text over the network.
- Each user brings their own supplier API keys: do not share an instance publicly on yours.
