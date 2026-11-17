// R1: `unsafe` outside the allow-listed native modules is reported (block, fn, extern block, unsafe impl).
unsafe extern "C" {
    fn abs(value: i32) -> i32;
}

unsafe fn raw() {}

unsafe trait Marker {}
unsafe impl Marker for u8 {}

fn main() {
    let _ = unsafe { abs(-1) };
    unsafe { raw() }
}
