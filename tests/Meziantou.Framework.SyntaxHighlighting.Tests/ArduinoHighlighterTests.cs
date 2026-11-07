namespace Meziantou.Framework.SyntaxHighlighting.Tests;

public class ArduinoHighlighterTests
{
    [Fact]
    public void Blink()
    {
        AssertHighlighter("arduino",
"""
// the setup function runs once when you press reset or power the board
void setup() {
  // initialize digital pin LED_BUILTIN as an output.
  pinMode(LED_BUILTIN, OUTPUT);
}

// the loop function runs over and over again forever
void loop() {
  digitalWrite(LED_BUILTIN, HIGH);  // turn the LED on (HIGH is the voltage level)
  delay(1000);                      // wait for a second
  digitalWrite(LED_BUILTIN, LOW);   // turn the LED off by making the voltage LOW
  delay(1000);                      // wait for a second
}
""",
"""
<span class="hljs-comment">// the setup function runs once when you press reset or power the board</span>
<span class="hljs-function"><span class="hljs-type">void</span> <span class="hljs-title">setup</span><span class="hljs-params">()</span> </span>{
  <span class="hljs-comment">// initialize digital pin LED_BUILTIN as an output.</span>
  <span class="hljs-built_in">pinMode</span>(<span class="hljs-literal">LED_BUILTIN</span>, <span class="hljs-literal">OUTPUT</span>);
}

<span class="hljs-comment">// the loop function runs over and over again forever</span>
<span class="hljs-function"><span class="hljs-type">void</span> <span class="hljs-title">loop</span><span class="hljs-params">()</span> </span>{
  <span class="hljs-built_in">digitalWrite</span>(<span class="hljs-literal">LED_BUILTIN</span>, <span class="hljs-literal">HIGH</span>);  <span class="hljs-comment">// turn the LED on (HIGH is the voltage level)</span>
  <span class="hljs-built_in">delay</span>(<span class="hljs-number">1000</span>);                      <span class="hljs-comment">// wait for a second</span>
  <span class="hljs-built_in">digitalWrite</span>(<span class="hljs-literal">LED_BUILTIN</span>, <span class="hljs-literal">LOW</span>);   <span class="hljs-comment">// turn the LED off by making the voltage LOW</span>
  <span class="hljs-built_in">delay</span>(<span class="hljs-number">1000</span>);                      <span class="hljs-comment">// wait for a second</span>
}
""");
    }

    [Fact]
    public void Serial()
    {
        AssertHighlighter("arduino",
"""
void setup() {
  Serial.begin(9600);
  while (!Serial) {
    ; // wait for serial port to connect
  }
  Serial.println("Hello, World!");
}
""",
"""
<span class="hljs-function"><span class="hljs-type">void</span> <span class="hljs-title">setup</span><span class="hljs-params">()</span> </span>{
  <span class="hljs-built_in">Serial</span>.<span class="hljs-built_in">begin</span>(<span class="hljs-number">9600</span>);
  <span class="hljs-keyword">while</span> (!<span class="hljs-built_in">Serial</span>) {
    ; <span class="hljs-comment">// wait for serial port to connect</span>
  }
  <span class="hljs-built_in">Serial</span>.<span class="hljs-built_in">println</span>(<span class="hljs-string">&quot;Hello, World!&quot;</span>);
}
""");
    }

    [Fact]
    public void AnalogRead()
    {
        AssertHighlighter("arduino",
"""
const int sensorPin = A0;
int sensorValue = 0;

void loop() {
  sensorValue = analogRead(sensorPin);
  float voltage = sensorValue * (5.0 / 1023.0);
  Serial.println(voltage);
}
""",
"""
<span class="hljs-type">const</span> <span class="hljs-type">int</span> sensorPin = A0;
<span class="hljs-type">int</span> sensorValue = <span class="hljs-number">0</span>;

<span class="hljs-function"><span class="hljs-type">void</span> <span class="hljs-title">loop</span><span class="hljs-params">()</span> </span>{
  sensorValue = <span class="hljs-built_in">analogRead</span>(sensorPin);
  <span class="hljs-type">float</span> voltage = sensorValue * (<span class="hljs-number">5.0</span> / <span class="hljs-number">1023.0</span>);
  <span class="hljs-built_in">Serial</span>.<span class="hljs-built_in">println</span>(voltage);
}
""");
    }

    [Fact]
    public void ArduinoTypes()
    {
        AssertHighlighter("arduino",
"""
boolean flag = true;
byte b = 0x1F;
word w = 65535;
String message = String("value: ") + 42;
unsigned long previousMillis = 0;
uint8_t pin = 13;
""",
"""
<span class="hljs-type">boolean</span> flag = <span class="hljs-literal">true</span>;
<span class="hljs-type">byte</span> b = <span class="hljs-number">0x1F</span>;
<span class="hljs-type">word</span> w = <span class="hljs-number">65535</span>;
<span class="hljs-type">String</span> message = <span class="hljs-built_in">String</span>(<span class="hljs-string">&quot;value: &quot;</span>) + <span class="hljs-number">42</span>;
<span class="hljs-type">unsigned</span> <span class="hljs-type">long</span> previousMillis = <span class="hljs-number">0</span>;
<span class="hljs-type">uint8_t</span> pin = <span class="hljs-number">13</span>;
""");
    }

    [Fact]
    public void IncludeAndDefine()
    {
        AssertHighlighter("arduino",
"""
#include <Servo.h>
#include <Wire.h>
#include "config.h"
#define LED_PIN 13

Servo myservo;
""",
"""
<span class="hljs-meta">#<span class="hljs-keyword">include</span> <span class="hljs-string">&lt;Servo.h&gt;</span></span>
<span class="hljs-meta">#<span class="hljs-keyword">include</span> <span class="hljs-string">&lt;Wire.h&gt;</span></span>
<span class="hljs-meta">#<span class="hljs-keyword">include</span> <span class="hljs-string">&quot;config.h&quot;</span></span>
<span class="hljs-meta">#<span class="hljs-keyword">define</span> LED_PIN 13</span>

<span class="hljs-built_in">Servo</span> myservo;
""");
    }

    [Fact]
    public void LibraryClasses()
    {
        AssertHighlighter("arduino",
"""
byte mac[] = { 0xDE, 0xAD, 0xBE, 0xEF, 0xFE, 0xED };
IPAddress ip(192, 168, 1, 177);
EthernetServer server(80);

void setup() {
  Ethernet.begin(mac, ip);
  server.begin();
}
""",
"""
<span class="hljs-type">byte</span> mac[] = { <span class="hljs-number">0xDE</span>, <span class="hljs-number">0xAD</span>, <span class="hljs-number">0xBE</span>, <span class="hljs-number">0xEF</span>, <span class="hljs-number">0xFE</span>, <span class="hljs-number">0xED</span> };
<span class="hljs-function"><span class="hljs-built_in">IPAddress</span> <span class="hljs-title">ip</span><span class="hljs-params">(<span class="hljs-number">192</span>, <span class="hljs-number">168</span>, <span class="hljs-number">1</span>, <span class="hljs-number">177</span>)</span></span>;
<span class="hljs-function"><span class="hljs-built_in">EthernetServer</span> <span class="hljs-title">server</span><span class="hljs-params">(<span class="hljs-number">80</span>)</span></span>;

<span class="hljs-function"><span class="hljs-type">void</span> <span class="hljs-title">setup</span><span class="hljs-params">()</span> </span>{
  <span class="hljs-built_in">Ethernet</span>.<span class="hljs-built_in">begin</span>(mac, ip);
  server.<span class="hljs-built_in">begin</span>();
}
""");
    }

    [Fact]
    public void Interrupt()
    {
        AssertHighlighter("arduino",
"""
volatile byte state = LOW;

void setup() {
  pinMode(2, INPUT_PULLUP);
  attachInterrupt(digitalPinToInterrupt(2), blink, CHANGE);
}

void blink() {
  state = !state;
}
""",
"""
<span class="hljs-keyword">volatile</span> <span class="hljs-type">byte</span> state = <span class="hljs-literal">LOW</span>;

<span class="hljs-function"><span class="hljs-type">void</span> <span class="hljs-title">setup</span><span class="hljs-params">()</span> </span>{
  <span class="hljs-built_in">pinMode</span>(<span class="hljs-number">2</span>, <span class="hljs-literal">INPUT_PULLUP</span>);
  <span class="hljs-built_in">attachInterrupt</span>(<span class="hljs-built_in">digitalPinToInterrupt</span>(<span class="hljs-number">2</span>), blink, CHANGE);
}

<span class="hljs-function"><span class="hljs-type">void</span> <span class="hljs-title">blink</span><span class="hljs-params">()</span> </span>{
  state = !state;
}
""");
    }

    [Fact]
    public void Class()
    {
        AssertHighlighter("arduino",
"""
class Led {
  public:
    Led(int pin) : _pin(pin) { pinMode(pin, OUTPUT); }
    void on() { digitalWrite(_pin, HIGH); }
  private:
    int _pin;
};
""",
"""
<span class="hljs-keyword">class</span> <span class="hljs-title class_">Led</span> {
  <span class="hljs-keyword">public</span>:
    <span class="hljs-built_in">Led</span>(<span class="hljs-type">int</span> pin) : _pin(pin) { <span class="hljs-built_in">pinMode</span>(pin, <span class="hljs-literal">OUTPUT</span>); }
    <span class="hljs-function"><span class="hljs-type">void</span> <span class="hljs-title">on</span><span class="hljs-params">()</span> </span>{ <span class="hljs-built_in">digitalWrite</span>(_pin, <span class="hljs-literal">HIGH</span>); }
  <span class="hljs-keyword">private</span>:
    <span class="hljs-type">int</span> _pin;
};
""");
    }

    [Fact]
    public void Millis()
    {
        AssertHighlighter("arduino",
"""
unsigned long currentMillis = millis();
if (currentMillis - previousMillis >= interval) {
  previousMillis = currentMillis;
  ledState = (ledState == LOW) ? HIGH : LOW;
}
""",
"""
<span class="hljs-type">unsigned</span> <span class="hljs-type">long</span> currentMillis = <span class="hljs-built_in">millis</span>();
<span class="hljs-keyword">if</span> (currentMillis - previousMillis &gt;= interval) {
  previousMillis = currentMillis;
  ledState = (ledState == <span class="hljs-literal">LOW</span>) ? <span class="hljs-literal">HIGH</span> : <span class="hljs-literal">LOW</span>;
}
""");
    }

    [Fact]
    public void Progmem()
    {
        AssertHighlighter("arduino",
"""
const char message[] PROGMEM = "Stored in flash";
static const uint16_t table[] = {1, 2, 3};
""",
"""
<span class="hljs-type">const</span> <span class="hljs-type">char</span> message[] PROGMEM = <span class="hljs-string">&quot;Stored in flash&quot;</span>;
<span class="hljs-type">static</span> <span class="hljs-type">const</span> <span class="hljs-type">uint16_t</span> table[] = {<span class="hljs-number">1</span>, <span class="hljs-number">2</span>, <span class="hljs-number">3</span>};
""");
    }

    [Fact]
    public void CppLiterals()
    {
        AssertHighlighter("arduino",
"""
auto p = nullptr; bool ok = false; int *q = NULL;
""",
"""
<span class="hljs-keyword">auto</span> p = <span class="hljs-literal">nullptr</span>; <span class="hljs-type">bool</span> ok = <span class="hljs-literal">false</span>; <span class="hljs-type">int</span> *q = <span class="hljs-literal">NULL</span>;
""");
    }

    [Fact]
    public void AnalogReferenceConstants()
    {
        AssertHighlighter("arduino",
"""
analogReference(EXTERNAL);
analogReference(INTERNAL1V1);
analogReference(DEFAULT);
""",
"""
<span class="hljs-built_in">analogReference</span>(<span class="hljs-literal">EXTERNAL</span>);
<span class="hljs-built_in">analogReference</span>(<span class="hljs-literal">INTERNAL1V1</span>);
<span class="hljs-built_in">analogReference</span>(<span class="hljs-literal">DEFAULT</span>);
""");
    }

    [Fact]
    public void Wire()
    {
        AssertHighlighter("arduino",
"""
Wire.beginTransmission(0x68);
Wire.write(0x6B);
Wire.endTransmission(true);
""",
"""
<span class="hljs-built_in">Wire</span>.<span class="hljs-built_in">beginTransmission</span>(<span class="hljs-number">0x68</span>);
<span class="hljs-built_in">Wire</span>.<span class="hljs-built_in">write</span>(<span class="hljs-number">0x6B</span>);
<span class="hljs-built_in">Wire</span>.<span class="hljs-built_in">endTransmission</span>(<span class="hljs-literal">true</span>);
""");
    }

    [Fact]
    public void Template()
    {
        AssertHighlighter("arduino",
"""
template <typename T> T clamp(T v, T lo, T hi) { return v < lo ? lo : (v > hi ? hi : v); }
""",
"""
<span class="hljs-keyword">template</span> &lt;<span class="hljs-keyword">typename</span> T&gt; <span class="hljs-function">T <span class="hljs-title">clamp</span><span class="hljs-params">(T v, T lo, T hi)</span> </span>{ <span class="hljs-keyword">return</span> v &lt; lo ? lo : (v &gt; hi ? hi : v); }
""");
    }

    [Fact]
    public void EnumAndStruct()
    {
        AssertHighlighter("arduino",
"""
enum class Mode { Idle, Running };
struct Point { int x; int y; };
""",
"""
<span class="hljs-keyword">enum class</span> <span class="hljs-title class_">Mode</span> { Idle, Running };
<span class="hljs-keyword">struct</span> <span class="hljs-title class_">Point</span> { <span class="hljs-type">int</span> x; <span class="hljs-type">int</span> y; };
""");
    }

    [Fact]
    public void BlockComment()
    {
        AssertHighlighter("arduino",
"""
/*
  Blink
  Turns an LED on for one second.
*/
void loop() {}
""",
"""
<span class="hljs-comment">/*
  Blink
  Turns an LED on for one second.
*/</span>
<span class="hljs-function"><span class="hljs-type">void</span> <span class="hljs-title">loop</span><span class="hljs-params">()</span> </span>{}
""");
    }

    [Fact]
    public void UnterminatedString()
    {
        AssertHighlighter("arduino",
"""
void setup() {
  Serial.println("oops);
}
""",
"""
<span class="hljs-function"><span class="hljs-type">void</span> <span class="hljs-title">setup</span><span class="hljs-params">()</span> </span>{
  <span class="hljs-built_in">Serial</span>.<span class="hljs-built_in">println</span>(<span class="hljs-string">&quot;oops);
}</span>
""");
    }

    [Fact]
    public void InoAlias()
    {
        AssertHighlighter("ino",
"""
void loop() {
  tone(8, 440, 100);
}
""",
"""
<span class="hljs-function"><span class="hljs-type">void</span> <span class="hljs-title">loop</span><span class="hljs-params">()</span> </span>{
  <span class="hljs-built_in">tone</span>(<span class="hljs-number">8</span>, <span class="hljs-number">440</span>, <span class="hljs-number">100</span>);
}
""");
    }

    [Fact]
    public void EmptyInput()
    {
        AssertHighlighter("arduino",
"",
"");
    }
}
