import AppKit
import Darwin
import Foundation
import WebKit

final class AppDelegate: NSObject, NSApplicationDelegate, WKNavigationDelegate {
    private var window: NSWindow?
    private var webView: WKWebView?
    private var hostProcess: Process?
    private var hostUrl: URL?

    func applicationDidFinishLaunching(_ notification: Notification) {
        NSApp.setActivationPolicy(.regular)
        NSApp.activate(ignoringOtherApps: true)

        do {
            let url = try startHost()
            hostUrl = url
            createWindow(url: url)
        } catch {
            showFatalError(error)
        }
    }

    func applicationShouldTerminateAfterLastWindowClosed(_ sender: NSApplication) -> Bool {
        true
    }

    func applicationWillTerminate(_ notification: Notification) {
        stopHost()
    }

    private func startHost() throws -> URL {
        let bundle = Bundle.main
        let hostUrl = bundle.bundleURL
            .appendingPathComponent("Contents", isDirectory: true)
            .appendingPathComponent("MacOS", isDirectory: true)
            .appendingPathComponent("IoTCoWork", isDirectory: false)

        guard FileManager.default.isExecutableFile(atPath: hostUrl.path) else {
            throw IoTCoWorkAppError.missingHost(hostUrl.path)
        }

        let port = try reserveLoopbackPort()
        let appUrl = URL(string: "http://127.0.0.1:\(port)/")!

        let process = Process()
        process.executableURL = hostUrl
        process.currentDirectoryURL = hostUrl.deletingLastPathComponent()
        process.arguments = [
            "--no-open",
            "--urls",
            "http://127.0.0.1:\(port)"
        ]

        process.standardOutput = FileHandle.nullDevice
        process.standardError = FileHandle.nullDevice
        try process.run()
        hostProcess = process

        return appUrl
    }

    private func reserveLoopbackPort() throws -> Int {
        let socketFd = Darwin.socket(AF_INET, SOCK_STREAM, 0)
        guard socketFd >= 0 else {
            throw IoTCoWorkAppError.portUnavailable
        }
        defer {
            Darwin.close(socketFd)
        }

        var address = sockaddr_in()
        address.sin_len = UInt8(MemoryLayout<sockaddr_in>.size)
        address.sin_family = sa_family_t(AF_INET)
        address.sin_port = in_port_t(0).bigEndian
        address.sin_addr = in_addr(s_addr: Darwin.inet_addr("127.0.0.1"))

        let bindResult = withUnsafePointer(to: &address) { pointer in
            pointer.withMemoryRebound(to: sockaddr.self, capacity: 1) { socketAddress in
                Darwin.bind(socketFd, socketAddress, socklen_t(MemoryLayout<sockaddr_in>.size))
            }
        }
        guard bindResult == 0 else {
            throw IoTCoWorkAppError.portUnavailable
        }

        var boundAddress = sockaddr_in()
        var length = socklen_t(MemoryLayout<sockaddr_in>.size)
        let nameResult = withUnsafeMutablePointer(to: &boundAddress) { pointer in
            pointer.withMemoryRebound(to: sockaddr.self, capacity: 1) { socketAddress in
                Darwin.getsockname(socketFd, socketAddress, &length)
            }
        }
        guard nameResult == 0 else {
            throw IoTCoWorkAppError.portUnavailable
        }

        return Int(UInt16(bigEndian: boundAddress.sin_port))
    }

    private func createWindow(url: URL) {
        let configuration = WKWebViewConfiguration()
        configuration.preferences.javaScriptCanOpenWindowsAutomatically = true

        let webView = WKWebView(frame: .zero, configuration: configuration)
        webView.navigationDelegate = self
        webView.allowsBackForwardNavigationGestures = true
        self.webView = webView

        let window = NSWindow(
            contentRect: NSRect(x: 0, y: 0, width: 1280, height: 820),
            styleMask: [.titled, .closable, .miniaturizable, .resizable],
            backing: .buffered,
            defer: false)
        window.title = "IoTCoWork"
        window.center()
        window.contentView = webView
        window.makeKeyAndOrderFront(nil)
        self.window = window

        loadWhenReady(url: url, attemptsRemaining: 80)
    }

    private func loadWhenReady(url: URL, attemptsRemaining: Int) {
        guard attemptsRemaining > 0 else {
            webView?.loadHTMLString(
                "<html><body style='font-family:-apple-system;padding:32px'><h2>IoTCoWork failed to start</h2><p>The local host did not become ready.</p></body></html>",
                baseURL: nil)
            return
        }

        var request = URLRequest(url: url)
        request.timeoutInterval = 0.5

        URLSession.shared.dataTask(with: request) { [weak self] _, response, _ in
            guard let self else { return }

            if let http = response as? HTTPURLResponse, (200...499).contains(http.statusCode) {
                DispatchQueue.main.async {
                    self.webView?.load(URLRequest(url: url))
                }
                return
            }

            DispatchQueue.main.asyncAfter(deadline: .now() + 0.15) {
                self.loadWhenReady(url: url, attemptsRemaining: attemptsRemaining - 1)
            }
        }.resume()
    }

    private func stopHost() {
        guard let process = hostProcess, process.isRunning else {
            return
        }

        process.terminate()
        DispatchQueue.global().asyncAfter(deadline: .now() + 2) {
            if process.isRunning {
                kill(process.processIdentifier, SIGKILL)
            }
        }
    }

    private func showFatalError(_ error: Error) {
        let alert = NSAlert()
        alert.alertStyle = .critical
        alert.messageText = "IoTCoWork could not start"
        alert.informativeText = error.localizedDescription
        alert.addButton(withTitle: "Quit")
        alert.runModal()
        NSApp.terminate(nil)
    }
}

enum IoTCoWorkAppError: LocalizedError {
    case missingHost(String)
    case portUnavailable

    var errorDescription: String? {
        switch self {
        case .missingHost(let path):
            return "The bundled host executable was not found or is not executable: \(path)"
        case .portUnavailable:
            return "Could not reserve a local port for IoTCoWork."
        }
    }
}

let app = NSApplication.shared
let delegate = AppDelegate()
app.delegate = delegate
app.run()
