# Deployment status and handoff

`deploy/compose.production.yml` is a portable production example. It publishes no host port, persists the canonical `/data/runs` tree, requires an operator-supplied image reference and publisher token, and joins only an explicitly supplied proxy network.

The live rollout is deployed from `rothzeta/enoch` and registered in the private `rothzeta/jachin` source of truth. Doco-CD deploys the digest-pinned image to dock-core at `https://enoch.iyrin.men`. The publisher token is stored in OpenBao at `kv/doco/enoch:publisher_token` and injected at reconciliation time; its value is not stored in either repository.

## Verified estate shape

The existing Jachin configuration establishes this path:

1. Public `*.iyrin.men` TLS and Tinyauth live on `dock-hekal-shaar`.
2. Public services hosted on dock-core proxy through `core-ingress` to `https://10.77.77.2:443`.
3. The dock-core application network is the existing external Docker network `public_net`.
4. Dock-core application routers use the existing `edge-only@file` middleware so a LAN caller cannot bypass the public edge by forging the public Host header.
5. Doco-CD injects application secrets from OpenBao through `external_secrets`; plaintext tokens do not belong in Git.

These facts were checked in `/opt/dev/eden/jachin/.doco-cd.dock-core.yaml`, `hosts/dock-core/termix/docker-compose.yaml`, `hosts/dock-hekal-shaar/ingress/dynamic/52-termix.yaml`, and the repository `AGENTS.md`.

## Prerequisites before a Jachin change

1. Create or choose the Enoch source repository using an authorized human/workflow and add it as this local repository's remote.
2. Choose the approved registry path and build/publish the container. Record an immutable version and digest; do not use `latest`.
3. Decide whether Enoch's durable run records are irreplaceable content under Jachin's NAS golden rule. If so, provision the NFS export, mount, marker, and directory through Boaz before adding the stack. Otherwise document and approve the local-storage exception and its backup/restore design.
4. Generate the publisher token outside Git and add an approved OpenBao field for Doco-CD to inject as `ENOCH_TOKEN`. Do not record the value in either repository.
5. Confirm the runtime UID/GID and final storage path, then add the appropriate init-permissions or strict NFS media-check pattern.

## Jachin wiring after prerequisites exist

Prepare one isolated Jachin commit that updates every applicable consumer:

- `hosts/dock-core/enoch/`: pinned image/digest, no published port, `/data/runs` bind, health check, `public_net`, and an `edge-only@file` router for the `enoch.iyrin.men` host.
- `.doco-cd.dock-core.yaml`: stack entry and OpenBao-backed `ENOCH_TOKEN` external secret.
- `hosts/dock-hekal-shaar/ingress/dynamic/`: two ordered routers for the same host. A higher-priority `/api/v1/publish` prefix router has **no Tinyauth** because Enoch bearer authentication is the machine boundary; the lower-priority catch-all browser/read router uses `tinyauth`. Both target `core-ingress`.
- `hosts/dock-core/backup.yaml` and `MOUNTS.md`: the approved persistent path and protection model.
- Homepage, Glance, internal Gatus, public Gatus, `README.md`, and `HISTORY.md`, as required by Jachin's new-stack checklist.

Only add the Doco-CD target after the image, secret, storage, and any Boaz prerequisites exist. Then validate Compose with its real environment, commit without unrelated paths, push through the approved channel, observe Doco-CD, and verify the browser-auth and publisher-bypass routes separately.
