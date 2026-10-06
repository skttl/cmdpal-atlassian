# Builds, releases and publication

## Local development package

`scripts/build.ps1` creates x64 or ARM64 MSIX output. The initial development publisher is `CN=Links for Atlassian Development`; it is not the final Store identity. The package version in the manifest is `0.1.0.0`.

An unsigned package is a build artifact. For local testing, use Visual Studio package deployment in Windows Developer Mode, or sign with a development certificate whose subject matches the package publisher. Trust only your own development certificate, then install the signed package using `Add-AppxPackage`. Never distribute the private key. This machine's installed UI has not yet been used to verify all acceptance flows.

## Signed GitHub Releases

Before tagging a release, configure:

- Repository secret `SIGNING_PFX_BASE64` with the publisher's signing PFX.
- Repository secret `SIGNING_PFX_PASSWORD` with its password.
- Repository variable `PACKAGE_PUBLISHER` with the certificate's exact subject.

The release job imports the PFX into the ephemeral runner's certificate store, signs/verifies packages by thumbprint, and removes both the temporary file and imported certificate. The password is never passed on a command line. Use a signing identity trusted by your target users for public sideload distribution; a self-signed development identity requires explicit certificate trust and is not a normal public installer.

Update the manifest version, commit the change, then tag `vMAJOR.MINOR.PATCH`. The workflow validates the tag against `MAJOR.MINOR.PATCH.0`, runs checks, builds both architectures and publishes MSIX files and SHA256 checksums. Missing signing configuration causes a failed release, rather than publishing unsigned installers.

## Microsoft Store

Reserve **Links for Atlassian** in Partner Center. Apply its exact identity name, publisher and publisher display name to the manifest before generating Store upload packages. Build with `-Store -Publisher '<Partner Center publisher>'`, create/upload the requested package bundle and supply screenshots, privacy URL and reviewer instructions for PowerToys and OAuth. Do not reuse the development package identity as the Store identity.

Store submission/certification is separate from GitHub Releases. Public Store links are added only after the listing is approved.

## GitHub Pages and gallery

Enable GitHub Pages with GitHub Actions as the source. The website workflow deploys `docs/site/`. It deploys no broker changes.

After Store approval, submit `extensions/skttl/links-for-atlassian/` to [microsoft/CmdPal-Extensions](https://github.com/microsoft/CmdPal-Extensions/blob/main/docs/CONTRIBUTING.md), using stable ID `skttl.links-for-atlassian`, the actual Store product ID, an original PNG icon no larger than 100 KB and up to five screenshots. Maintainers review the submission and regenerate the catalog. GitHub Releases alone do not qualify as an install source.

The gallery metadata/icon/screenshots will be finalized after installed-UI acceptance and a real Store product ID exist. No submission has been made.
