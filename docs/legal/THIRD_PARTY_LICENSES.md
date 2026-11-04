# X-Shard — Component licenses and notices

## Independent components

- **X-Shard GUI:** GNU GPL version 3; the publisher's supplied declaration and notices follow.
- **extract-xiso:** original modified Berkeley license. Maintained by **XboxDev**; originally written by **in <in@fishtank.com>**.
- **XGDTool:** GNU GPL version 3, by **wiredopposite**. Original repository: https://github.com/wiredopposite/XGDTool.
- Framework and native-library notices are included in the `licenses` directory.

The backends are independent executable programs. Their original licenses govern those components. The X-Shard GUI notices and EULA do not replace, restrict or override rights granted by those third-party licenses. The full GPLv3 text is included below and in `licenses/GPL-3.0.txt`; the upstream XGDTool source/build archive is included in `sources`.

This product includes software developed by in <in@fishtank.com>.

## Local application data

X-Shard stores settings, source/destination paths, queue metadata, and the legal-document content hash and acceptance timestamp locally. Processing logs may contain local paths and can be exported locally. These data are not automatically sent to the developer or a third party. XGDTool defaults to offline mode. If you explicitly disable Offline mode for a new job, XGDTool may contact Google (connectivity check), GitHub/Repackinator (title database) and XboxUnity (title IDs and artwork). Some upstream requests use unencrypted HTTP. Title IDs and connection metadata can be disclosed to those services. X-Shard does not contact those services itself; the selected backend performs these optional lookups. The bundled publisher privacy declaration describes the default offline workflow; this section documents the optional online backend feature. External web links open in the system browser only after an explicit click.
