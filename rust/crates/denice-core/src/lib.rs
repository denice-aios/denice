// Copyright (c) Denice.ai. All rights reserved.

//! Shared pieces of the Denice loader (`denice`) and the versioned entry (`denice-v<version>-<rid>`):
//! the product version and its order, the install layout, the loader protocol and the platform facts.

pub mod layout;
pub mod platform;
pub mod protocol;
pub mod swap;
pub mod version;
