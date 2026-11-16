// R1: the same code in an allow-listed native module is not reported.
unsafe extern "C" {
    fn abs(value: i32) -> i32;
}

fn main() {
    let _ = unsafe { abs(-1) };
}
