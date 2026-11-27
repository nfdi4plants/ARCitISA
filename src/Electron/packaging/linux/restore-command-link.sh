#!/bin/bash
# Sets up /usr/bin/arcitisa again at the end of an update: rpm runs it as %posttrans, pacman as
# post_upgrade. electron-builder's install script creates the link, but on an update it can be gone
# afterwards:
# - rpm deletes the files of the old package after the install script of the new one has run, and
#   the Forge-built 3.4.0 rpm owns /usr/bin/arcitisa.
# - The remove script of electron-builder's rpm deletes the link on every update, also after the
#   install script of the new package.
# - pacman runs post_upgrade instead of post_install on an update, and the 3.4.0-rc.2 package owns
#   /usr/bin/arcitisa, so pacman deletes it.
# Same link as electron-builder's install script (templates/linux/after-install.tpl). The paths
# follow productName and linux.executableName in electron-builder.config.js.

if type update-alternatives >/dev/null 2>&1; then
    # A link that does not go through update-alternatives is left from an older package.
    if [ -L '/usr/bin/arcitisa' ] && [ "$(readlink '/usr/bin/arcitisa')" != '/etc/alternatives/arcitisa' ]; then
        rm -f '/usr/bin/arcitisa'
    fi
    # Registering again may keep a deleted link deleted, so register from scratch.
    update-alternatives --remove 'arcitisa' '/opt/ARCitISA/arcitisa' >/dev/null 2>&1 || true
    update-alternatives --install '/usr/bin/arcitisa' 'arcitisa' '/opt/ARCitISA/arcitisa' 100 || ln -sf '/opt/ARCitISA/arcitisa' '/usr/bin/arcitisa'
else
    ln -sf '/opt/ARCitISA/arcitisa' '/usr/bin/arcitisa'
fi
