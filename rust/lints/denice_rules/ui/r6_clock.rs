// R6: no clock reads or sleeps in product code.
fn main() {
    let _ = std::time::SystemTime::now();
    let _ = std::time::Instant::now();
    std::thread::sleep(std::time::Duration::from_millis(1));
}
