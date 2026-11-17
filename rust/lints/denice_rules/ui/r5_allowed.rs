// R5: an allowed file may ask for the program's own path.
fn main() {
    let _ = std::env::current_exe();
}
