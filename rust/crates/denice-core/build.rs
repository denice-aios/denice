// Copyright (c) Denice.ai. All rights reserved.

//! Stamps the product version. CI computes it once (eng/compute-build-id.sh) and passes `DENICE_VERSION`;
//! a local developer build without it is stamped `0.0.0-local+local`, which sorts below every real release.

fn main() {
    println!("cargo:rerun-if-env-changed=DENICE_VERSION");
    let version = std::env::var("DENICE_VERSION").unwrap_or_else(|_| "0.0.0-local+local".to_owned());
    println!("cargo:rustc-env=DENICE_BUILD_VERSION={version}");
}
