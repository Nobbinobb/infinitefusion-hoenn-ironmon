module IronmonCollectionStartupTests
  class StatusConnection
    attr_accessor :ready, :supported, :drained
    attr_reader :events

    def initialize
      @ready = @supported = @drained = true
      @events = []
    end

    def session_ready?; return @ready; end
    def collection_supported?; return @supported; end
    def output_drained?; return @drained; end
    def send_event(name, payload); @events << [name, payload]; end
  end

  def self.assert(condition, message)
    raise "Collection startup validation failed: #{message}" if !condition
  end

  def self.run
    collection = Ironmon::GameplayCollection
    evidence = collection::NativeEvidence
    assert(evidence.instance_variable_get(:@installed) == true, "native evidence must install before gameplay: #{collection.last_error}")
    assert(collection.last_error.nil?, "startup must not silently disable capture: #{collection.last_error}")
    definitions = evidence::NATIVE_DEFINITIONS + evidence::SOURCE_OBSERVATIONS + evidence::HP_CALLSITES.values
    definitions.each do |definition|
      source = File.binread(File.join("Data", "Scripts", definition["path"]))
      assert(evidence.source_digest(source) == definition["sha256"], "every native source uses the same line-ending-independent identity")
    end

    definition = evidence::NATIVE_DEFINITIONS.first
    source = File.binread(File.join("Data", "Scripts", definition["path"]))
    tampered = File.join($ironmon_smoke_output_root, File.basename(definition["path"]))
    File.binwrite(tampered, source + "\n# unexpected native modification\n")
    rejected = false
    begin
      evidence.install_definition(Object.const_get(definition["owner"]), tampered, definition)
    rescue RuntimeError => error
      rejected = error.message.start_with?("Native collection evidence source changed:")
    end
    assert(rejected, "real source changes must still be rejected")
    test_startup_status(collection)
  end

  def self.test_startup_status(collection)
    names = [:@journal, :@last_error, :@last_reported_error, :@disabled, :@status_sent_at]
    saved = names.to_h { |name| [name, collection.instance_variable_get(name)] }
    begin
      collection.instance_variable_set(:@journal, nil)
      collection.instance_variable_set(:@status_sent_at, nil)
      collection.fail_collection(IOError.new("isolated collector startup failure"))
      connection = StatusConnection.new
      collection.transport_tick(connection)
      assert(connection.events.length == 1 && connection.events[0][0] == "collection_status", "startup failures are reported without an initialized journal")
      status = connection.events[0][1]
      assert(status["status"] == "error" && status["capture_disabled"] == true && status["last_error"].include?("isolated collector startup failure"), "status retains the original collector failure")
      assert(status["journal"].nil? && collection.instance_variable_get(:@journal).nil?, "reporting cannot create a producer profile")
      collection.transport_tick(connection)
      assert(connection.events.length == 1, "status remains bounded to its reporting interval")
      [:ready, :supported, :drained].each do |flag|
        blocked = StatusConnection.new
        blocked.public_send("#{flag}=", false)
        collection.instance_variable_set(:@status_sent_at, nil)
        collection.transport_tick(blocked)
        assert(blocked.events.empty?, "status respects session negotiation and output backpressure")
      end
    ensure
      saved.each { |name, value| collection.instance_variable_set(name, value) }
    end
  end
end

IronmonCollectionStartupTests.run
