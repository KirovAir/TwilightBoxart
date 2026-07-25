# BlocksDS ROM makefile. `make` with BlocksDS installed, or use the container:
#   docker run --rm -v "$PWD:/work" -w /work skylyrac/blocksds:slim-latest make

BLOCKSDS	?= /opt/blocksds/core
BLOCKSDSEXT	?= /opt/blocksds/external

NAME		:= twilightboxart

# The version lives in Directory.Build.props, which is what every .NET project here builds from.
# Taking major.minor from it means a bump in one place reaches the ROM header, the on-screen label
# and the User-Agent together, instead of three copies drifting apart.
APP_VERSION	:= $(shell sed -n 's:.*<Version>\([0-9]*\.[0-9]*\).*:\1:p' ../Directory.Build.props)
ifeq ($(APP_VERSION),)
$(error Could not read <Version> from ../Directory.Build.props)
endif

GAME_TITLE	:= TwilightBoxart
GAME_SUBTITLE	:= Box art for TWiLightMenu++ & Pico $(APP_VERSION)
GAME_AUTHOR	:= KirovAir

SOURCEDIRS	:= source mbedtls/library
INCLUDEDIRS	:= include mbedtls/include
GFXDIRS		:= graphics
BINDIRS		:= data
AUDIODIRS	:=
NITROFSDIR	:=

LIBS		:= -ldswifi9 -lnds9
# dswifi-patched first, so the DHCP-broadcast-flag build wins over the SDK's own copy.
LIBDIRS		:= dswifi-patched $(BLOCKSDS)/libs/dswifi $(BLOCKSDS)/libs/libnds

# The default ARM7 core has no WiFi driver at all; the radio lives on the ARM7, so without this
# the ARM9 waits forever for a WiFi handler that does not exist.
ARM7ELF		:= $(BLOCKSDS)/sys/arm7/main_core/arm7_dswifi.elf

DEFINES		?=
DEFINES		+= -DMBEDTLS_USER_CONFIG_FILE=\"tb_mbedtls_config.h\"
DEFINES		+= -DAPP_VERSION=\"$(APP_VERSION)\"

# Build-time overrides, for a test ROM against a staging backend where editing the ini on the card
# first defeats the point. Its own variable because a command-line DEFINES= would replace the two
# above rather than add to them, and the build then fails on a missing APP_VERSION:
#   make EXTRA_DEFINES='-DDEFAULT_BACKEND_HOST=\"my.tunnel.example\"'
EXTRA_DEFINES	?=
DEFINES		+= $(EXTRA_DEFINES)

include $(BLOCKSDS)/sys/default_makefiles/rom_arm9/Makefile
