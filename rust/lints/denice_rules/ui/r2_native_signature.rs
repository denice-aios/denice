// R2: native signatures may use only C types.
#![allow(improper_ctypes, improper_ctypes_definitions)]

#[repr(C)]
pub struct CPoint {
    x: i32,
}

pub struct RustPoint {
    x: i32,
}

pub type Good = unsafe extern "C" fn(i32, *const u8, CPoint, Option<extern "C" fn()>) -> f64;
pub type BadBool = unsafe extern "C" fn(bool) -> i32;
pub type BadRef = unsafe extern "C" fn(&u8);
pub type BadStruct = unsafe extern "C" fn() -> RustPoint;
pub type RustAbiIsNotNative = fn(bool, &str) -> char;

unsafe extern "C" {
    fn takes_char(c: char);
}

fn main() {
    let _ = RustPoint { x: CPoint { x: 1 }.x };
    let _ = takes_char;
}
