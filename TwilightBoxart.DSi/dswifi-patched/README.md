# Patched dswifi (ARM9)

`libdswifi9.a` is dswifi at tag `v1.22.2-blocks`, rebuilt with `dhcp-broadcast-flag.patch` and
BlocksDS 1.22.2:
lwip's DHCP assumes the NIC can receive unicast before an address is configured, which the DS
radio path cannot, so servers that unicast their offers (udhcpd, phone hotspots) never complete
DHCP. The patch sets the RFC 2131 broadcast flag while we have no address, making every server
broadcast its replies. Upstream issue: codeberg.org/blocksds/sdk/issues/325.

Rebuild:

```bash
git clone --branch v1.22.2-blocks https://github.com/blocksds/dswifi /tmp/dswifi-src
git -C /tmp/dswifi-src apply $(pwd)/dhcp-broadcast-flag.patch
docker run --rm -v /tmp/dswifi-src:/work -w /work skylyrac/blocksds:slim-latest \
  make -f Makefile.arm9 ENABLE_LWIP=1 BLOCKSDS=/opt/wonderful/thirdparty/blocksds/core
cp /tmp/dswifi-src/lib/libdswifi9.a lib/
```

Headers stay the SDK's own (the DHCP patch changes no interface), but the archive and application
must use the same `fcntl` constants. The compile-time guard in `source/networking.c` catches a
mismatch; when it fires, rebuild the archive against the new SDK and bump the guard with it.
Drop this folder once the DHCP fix lands upstream.
