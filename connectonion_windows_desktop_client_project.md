# 99 - ConnectOnion Windows Desktop Client: Native Access to AI Agents

**Client:** Aaron Xie  
**Allowed groups:** 3 groups

## Project background & goals

ConnectOnion is an open-source AI agent framework that enables users to build and interact with remotely accessible AI agents. Existing access to ConnectOnion agents is primarily provided through web-based or command-line interfaces.

The goal of this project is to design and implement a Windows desktop client that allows users to interact with ConnectOnion agents through a dedicated desktop application. The application should provide a convenient and reliable experience for Windows users, supporting agent connection, chat interaction, local conversation management and desktop-oriented usability features.

The outcome will be a functional Windows application prototype that extends the ConnectOnion ecosystem beyond browser-based access and provides a foundation for future desktop productivity features.

## Project scope

This project focuses on the design and development of a Windows desktop client application for ConnectOnion.

The student team will investigate the existing ConnectOnion open-source framework, documentation, web client and relevant communication mechanisms. Based on this analysis, the team will design an appropriate Windows desktop application architecture and implement an end-to-end client prototype.

The minimum project scope includes:

- Development of a Windows desktop application using an appropriate technology stack.
- Connection to one or more remotely accessible ConnectOnion agents.
- A desktop chat interface for sending user messages and displaying agent responses.
- Support for response progress, streaming updates or execution status where supported.
- Local conversation/session history.
- Saved agent connection configurations.
- Secure handling of required credentials or connection information.
- Desktop-appropriate navigation, settings, status display and error handling.
- Testing, technical documentation and a demonstrable Windows build.

The project is not intended to be only a packaged version of the existing web interface. It should provide a desktop-oriented application experience suitable for ongoing extension and use on Windows.

Optional extensions may include system tray integration, desktop notifications, multiple agent workspaces, drag-and-drop file input, keyboard shortcuts and installable packaging.

## Project requirements

The student team is expected to design and implement a functional, maintainable and testable Windows desktop client for accessing ConnectOnion AI agents.

### Core functional requirements

#### 1. Agent Connection

The application must allow users to configure and connect to remote ConnectOnion agents using an appropriate address, endpoint or connection configuration. Connection status should be clearly displayed and invalid or unavailable connections should be handled gracefully.

#### 2. Desktop Chat Interaction

Users must be able to send messages to a connected agent and receive responses through a usable desktop chat interface. The interface should support message display, input handling, scrolling, loading feedback and response status indicators where appropriate.

#### 3. Response and Task Feedback

Where supported by the ConnectOnion communication interface, the application should display streaming responses or progress information. Users should receive clear feedback while an agent request is running, completed or failed.

#### 4. Conversation and Session Management

The application should support local conversation history and multiple chat sessions. Users should be able to start new conversations, reopen previous conversations and organise interactions with different agents.

#### 5. Agent Configuration Management

Users should be able to save, select, edit and remove agent connection configurations. Sensitive information, where required, should be handled using appropriate secure storage mechanisms available on Windows.

#### 6. Windows Desktop User Experience

The application should provide a desktop-oriented user experience, including appropriate window layout, keyboard and mouse interaction, readable presentation of long responses, settings management and support for light/dark display where feasible.

#### 7. Reliability and Error Handling

The application should handle network interruption, request timeout, failed agent responses, invalid configuration and reconnection scenarios with meaningful user-facing feedback.

#### 8. Testing and Documentation

The project should include testing for key functionality, including communication logic, conversation persistence, configuration handling and major user workflows. The final delivery should include source code, build instructions, architecture documentation and known limitations.

### Minimum deliverables

- Windows desktop application source code.
- A demonstrable executable or installable Windows build.
- Successful interaction with at least one ConnectOnion agent.
- Conversation history and agent configuration management.
- User-friendly connection and error handling.
- Testing evidence and technical documentation.
- Final demonstration and handover report.

### Optional stretch goals

- System tray integration.
- Desktop notifications for completed agent tasks.
- Multiple-agent workspace management.
- File drag-and-drop support.
- Keyboard shortcuts.
- Installer packaging and release-ready build.

## Required knowledge and skills

Students should possess or be willing to develop skills in:

- Software engineering, version control, testing and technical documentation.
- Desktop application development using an appropriate framework.
- Client-server communication, APIs, asynchronous programming and network error handling.
- User interface and user experience design for desktop software.
- Git-based collaborative development workflows.

Desirable but not essential skills include:

- Experience with C#/.NET, WinUI, WPF, Electron, Tauri or comparable desktop development technologies.
- Experience with WebSocket communication or real-time applications.
- Experience with local persistence and secure configuration storage.
- Familiarity with AI applications or agent-based systems.
- Experience with application packaging or Windows deployment.

## Expected outcomes and deliverables

- Source code
- Documentation
- User guide

## Disciplines related to the project

- Software Development
- Generative AI (GenAI)
- Human Computer Interaction (HCI)

## Resources provided

- OpenOnion architecture source code
- Selected LLM APIs
