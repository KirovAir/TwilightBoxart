# Patched dswifi (ARM9)

`libdswifi9.a` is dswifi at tag `v1.22.1-blocks`, rebuilt with `dhcp-broadcast-flag.patch`:
lwip's DHCP assumes the NIC can receive unicast before an address is configured, which the DS
radio path cannot, so servers that unicast their offers (udhcpd, phone hotspots) never complete
DHCP. The patch sets the RFC 2131 broadcast flag while we have no address, making every server
broadcast its replies. Upstream issue: codeberg.org/blocksds/sdk/issues/325.

Rebuild:

```bash
git clone --branch v1.22.1-blocks https://github.com/blocksds/dswifi /tmp/dswifi-src
git -C /tmp/dswifi-src apply $(pwd)/dhcp-broadcast-flag.patch
docker run --rm -v /tmp/dswifi-src:/work -w /work skylyrac/blocksds:slim-latest \
  make -f Makefile.arm9 ENABLE_LWIP=1 BLOCKSDS=/opt/wonderful/thirdparty/blocksds/core
cp /tmp/dswifi-src/lib/libdswifi9.a lib/
```

Headers stay the SDK's own (the patch changes no interface). Drop this folder once the fix
lands upstream.
